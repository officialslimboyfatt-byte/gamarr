using Gamarr.Application.Contracts;
using Gamarr.Application.Exceptions;
using Gamarr.Application.Interfaces;
using Gamarr.Application.Services;
using Gamarr.Domain.Entities;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamarr.Infrastructure.Services;

public sealed class JobService(GamarrDbContext dbContext, IJobDispatchPublisher publisher) : IJobService
{
    public async Task<JobResponse> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidateJobRequest(request);

        var package = await dbContext.Packages.Include(p => p.Versions).ThenInclude(v => v.Media)
            .Include(p => p.Versions).ThenInclude(v => v.DetectionRules)
            .Include(p => p.Versions).ThenInclude(v => v.Prerequisites)
            .FirstOrDefaultAsync(p => p.Id == request.PackageId, cancellationToken)
            ?? throw new AppNotFoundException("Package not found.");
        var machine = await dbContext.Machines.FirstOrDefaultAsync(m => m.Id == request.MachineId, cancellationToken)
            ?? throw new AppNotFoundException("Machine not found.");
        var version = package.Versions.FirstOrDefault(v => v.IsActive)
            ?? throw new AppValidationException("Package has no active version.");

        if (machine.Status == MachineStatus.Offline)
        {
            throw new AppConflictException("Target machine is offline.");
        }

        var machineHasActiveJob = await dbContext.Jobs.AnyAsync(
            j => j.MachineId == machine.Id &&
                 j.State != JobState.Completed &&
                 j.State != JobState.Failed &&
                 j.State != JobState.Cancelled,
            cancellationToken);

        if (machineHasActiveJob)
        {
            throw new AppConflictException("Target machine already has an active job.");
        }

        var job = new Job
        {
            PackageId = package.Id,
            PackageVersionId = version.Id,
            MachineId = machine.Id,
            ActionType = request.ActionType,
            RequestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "local-admin" : request.RequestedBy.Trim(),
            State = JobState.Queued,
            Events =
            {
                new JobEvent
                {
                    SequenceNumber = 1,
                    State = JobState.Queued,
                    Message = "Job queued."
                }
            }
        };

        dbContext.Jobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        await publisher.PublishJobCreatedAsync(job.Id, machine.Id, cancellationToken);
        var loaded = await Query().FirstAsync(x => x.Id == job.Id, cancellationToken);
        return loaded.ToResponse();
    }

    public async Task<IReadOnlyCollection<JobResponse>> ListAsync(
        Guid? machineId,
        JobState? state,
        JobActionType? actionType,
        string? search,
        string? scope,
        CancellationToken cancellationToken)
    {
        var query = Query();

        if (machineId.HasValue)
        {
            query = query.Where(j => j.MachineId == machineId.Value);
        }

        if (state.HasValue)
        {
            query = query.Where(j => j.State == state.Value);
        }

        if (actionType.HasValue)
        {
            query = query.Where(j => j.ActionType == actionType.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(j =>
                (j.Package != null && EF.Functions.ILike(j.Package.Name, $"%{term}%")) ||
                (j.Machine != null && EF.Functions.ILike(j.Machine.Name, $"%{term}%")));
        }

        query = (scope ?? string.Empty).ToLowerInvariant() switch
        {
            "active" => query.Where(j => j.State != JobState.Completed && j.State != JobState.Failed && j.State != JobState.Cancelled),
            "history" => query.Where(j => j.State == JobState.Completed || j.State == JobState.Failed || j.State == JobState.Cancelled),
            _ => query
        };

        var jobs = await query.ToListAsync(cancellationToken);
        return jobs.Select(j => j.ToResponse()).ToArray();
    }

    public async Task<JobResponse?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await Query().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        return job?.ToResponse();
    }

    public async Task<JobResponse?> FindActiveAsync(Guid packageId, Guid machineId, CancellationToken cancellationToken)
    {
        var job = await Query().FirstOrDefaultAsync(
            j => j.PackageId == packageId &&
                 j.MachineId == machineId &&
                 j.State != JobState.Completed &&
                 j.State != JobState.Failed &&
                 j.State != JobState.Cancelled,
            cancellationToken);

        return job?.ToResponse();
    }

    public async Task<JobResponse?> ClaimAsync(Guid jobId, ClaimJobRequest request, CancellationToken cancellationToken)
    {
        if (request.MachineId == Guid.Empty)
        {
            throw new AppValidationException("MachineId is required to claim a job.");
        }

        var job = await dbContext.Jobs.Include(j => j.Events).FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || job.MachineId != request.MachineId || job.State != JobState.Queued)
        {
            return null;
        }

        var machine = await dbContext.Machines.FirstAsync(m => m.Id == request.MachineId, cancellationToken);

        job.State = JobState.Assigned;
        job.ClaimedAtUtc = DateTimeOffset.UtcNow;
        machine.Status = MachineStatus.Busy;
        dbContext.JobEvents.Add(new JobEvent
        {
            JobId = job.Id,
            SequenceNumber = job.Events.Count + 1,
            State = JobState.Assigned,
            Message = $"Job claimed by machine {request.MachineId}."
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await publisher.PublishJobUpdatedAsync(job.Id, cancellationToken);
        var loaded = await Query().FirstAsync(j => j.Id == job.Id, cancellationToken);
        return loaded.ToResponse();
    }

    public async Task<JobResponse?> AddEventAsync(Guid jobId, JobEventRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidateJobEvent(request);

        var job = await dbContext.Jobs.Include(j => j.Events).Include(j => j.Logs)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.Events.Any(e => e.SequenceNumber == request.SequenceNumber))
        {
            var existing = await Query().FirstAsync(x => x.Id == jobId, cancellationToken);
            return existing.ToResponse();
        }

        if (job.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
        {
            throw new AppConflictException("Terminal jobs cannot accept additional events.");
        }

        var currentSequence = job.Events.Count == 0 ? 0 : job.Events.Max(e => e.SequenceNumber);
        if (request.SequenceNumber != currentSequence + 1)
        {
            throw new AppConflictException($"Expected event sequence {currentSequence + 1}, received {request.SequenceNumber}.");
        }

        if (!JobStateRules.CanTransition(job.State, request.State) && job.State != request.State)
        {
            throw new AppConflictException($"Invalid job transition from {job.State} to {request.State}.");
        }

        if (job.State != request.State)
        {
            job.State = request.State;
        }

        dbContext.JobEvents.Add(new JobEvent
        {
            JobId = job.Id,
            SequenceNumber = request.SequenceNumber,
            State = request.State,
            Message = request.Message.Trim()
        });

        foreach (var log in request.Logs)
        {
            dbContext.PackageActionLogs.Add(new PackageActionLog
            {
                JobId = job.Id,
                Level = log.Level,
                Source = log.Source.Trim(),
                Message = log.Message.Trim(),
                PayloadJson = log.PayloadJson
            });
        }

        if (request.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
        {
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.OutcomeSummary = request.Message.Trim();
            var machine = await dbContext.Machines.FirstAsync(m => m.Id == job.MachineId, cancellationToken);
            machine.Status = MachineStatus.Online;

            if (request.State == JobState.Completed &&
                !string.IsNullOrWhiteSpace(request.LaunchExecutablePath))
            {
                var version = await dbContext.PackageVersions.FirstOrDefaultAsync(v => v.Id == job.PackageVersionId, cancellationToken);
                if (version is not null)
                {
                    version.LaunchExecutablePath = request.LaunchExecutablePath.Trim();
                }
            }

            if (request.State == JobState.Completed &&
                request.DetectedInstalled == false)
            {
                var version = await dbContext.PackageVersions.FirstOrDefaultAsync(v => v.Id == job.PackageVersionId, cancellationToken);
                if (version is not null)
                {
                    version.LaunchExecutablePath = null;
                }
            }

            if (request.State == JobState.Completed)
            {
                await UpsertMachineInstallAsync(job, request, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await publisher.PublishJobUpdatedAsync(job.Id, cancellationToken);
        var loaded = await Query().FirstAsync(x => x.Id == job.Id, cancellationToken);
        return loaded.ToResponse();
    }

    private async Task UpsertMachineInstallAsync(Job job, JobEventRequest request, CancellationToken cancellationToken)
    {
        if (job.ActionType == JobActionType.Launch)
        {
            return;
        }

        var version = await dbContext.PackageVersions.FirstOrDefaultAsync(v => v.Id == job.PackageVersionId, cancellationToken);
        if (version is null)
        {
            return;
        }

        var install = await dbContext.MachinePackageInstalls
            .FirstOrDefaultAsync(x => x.MachineId == job.MachineId && x.PackageVersionId == job.PackageVersionId, cancellationToken);

        var isNew = install is null;
        install ??= new MachinePackageInstall
        {
            MachineId = job.MachineId,
            PackageId = job.PackageId,
            PackageVersionId = job.PackageVersionId
        };

        install.PackageId = job.PackageId;
        install.PackageVersionId = job.PackageVersionId;
        install.LastValidatedAtUtc = DateTimeOffset.UtcNow;
        install.ValidationSummary = string.IsNullOrWhiteSpace(request.ValidationMessage) ? request.Message.Trim() : request.ValidationMessage.Trim();
        install.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.LaunchExecutablePath))
        {
            install.LastKnownLaunchPath = request.LaunchExecutablePath.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(version.LaunchExecutablePath))
        {
            install.LastKnownLaunchPath = version.LaunchExecutablePath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.InstallLocation))
        {
            install.LastKnownInstallLocation = request.InstallLocation.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ResolvedUninstallCommand))
        {
            install.ResolvedUninstallCommand = request.ResolvedUninstallCommand.Trim();
        }

        switch (job.ActionType)
        {
            case JobActionType.Install:
                install.State = "Installed";
                install.InstalledAtUtc ??= DateTimeOffset.UtcNow;
                break;
            case JobActionType.Validate:
                install.State = request.DetectedInstalled == false ? "NotInstalled" : "Installed";
                if (request.DetectedInstalled == true)
                {
                    install.InstalledAtUtc ??= DateTimeOffset.UtcNow;
                }
                break;
            case JobActionType.Uninstall:
                install.State = "NotInstalled";
                install.InstalledAtUtc = null;
                install.LastKnownLaunchPath = null;
                install.LastKnownInstallLocation = null;
                break;
        }

        if (isNew)
        {
            dbContext.MachinePackageInstalls.Add(install);
        }
    }

    public async Task<JobResponse> CancelJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.Jobs.Include(j => j.Events).Include(j => j.Logs)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new AppNotFoundException("Job not found.");

        if (job.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
        {
            throw new AppConflictException($"Job is already {job.State} and cannot be cancelled.");
        }

        var nextSequence = job.Events.Count == 0 ? 1 : job.Events.Max(e => e.SequenceNumber) + 1;

        job.State = JobState.Cancelled;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        job.OutcomeSummary = "Cancelled by user.";

        dbContext.JobEvents.Add(new JobEvent
        {
            JobId = job.Id,
            SequenceNumber = nextSequence,
            State = JobState.Cancelled,
            Message = "Cancelled by user."
        });

        if (job.MachineId != Guid.Empty)
        {
            var machine = await dbContext.Machines.FirstOrDefaultAsync(m => m.Id == job.MachineId, cancellationToken);
            if (machine is not null)
            {
                machine.Status = MachineStatus.Online;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await publisher.PublishJobUpdatedAsync(job.Id, cancellationToken);
        var loaded = await Query().FirstAsync(x => x.Id == job.Id, cancellationToken);
        return loaded.ToResponse();
    }

    public async Task<NextJobResponse?> GetNextJobAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var job = await dbContext.Jobs.Include(j => j.Package)
            .Include(j => j.PackageVersion!).ThenInclude(v => v.Media)
            .Include(j => j.PackageVersion!).ThenInclude(v => v.DetectionRules)
            .Include(j => j.PackageVersion!).ThenInclude(v => v.Prerequisites)
            .Where(j => j.MachineId == machineId && j.State == JobState.Queued)
            .OrderBy(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (job?.Package is null || job.PackageVersion is null)
        {
            return null;
        }

        var packageVersion = job.PackageVersion;
        var media = ResolveExecutionMedia(packageVersion);

        return new NextJobResponse(
            job.Id,
            job.PackageId,
            job.PackageVersionId,
            job.ActionType,
            job.Package.Name,
            packageVersion.VersionLabel,
            packageVersion.SupportedOs,
            packageVersion.Architecture,
            packageVersion.InstallScriptKind,
            packageVersion.InstallScriptPath,
            packageVersion.UninstallScriptPath,
            packageVersion.UninstallArguments,
            packageVersion.ManifestFormatVersion,
            packageVersion.ManifestJson,
            packageVersion.TimeoutSeconds,
            packageVersion.Notes,
            packageVersion.InstallStrategy,
            packageVersion.InstallerFamily,
            packageVersion.InstallerPath,
            packageVersion.SilentArguments,
            packageVersion.InstallDiagnostics,
            packageVersion.LaunchExecutablePath,
            media,
            packageVersion.DetectionRules.Select(d => new DetectionRuleResponse(d.Id, d.RuleType, d.Value)).ToArray(),
            packageVersion.Prerequisites.Select(p => new PrerequisiteResponse(p.Id, p.Name, p.Notes)).ToArray());
    }

    private static IReadOnlyCollection<PackageMediaResponse> ResolveExecutionMedia(PackageVersion packageVersion)
    {
        if (string.Equals(packageVersion.ProcessingState, "Ready", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(packageVersion.NormalizedAssetRootPath) &&
            NormalizedRootHasExecutableContent(packageVersion.NormalizedAssetRootPath))
        {
            return
            [
                new PackageMediaResponse(
                    Guid.Empty,
                    MediaType.InstallerFolder,
                    "normalized",
                    packageVersion.NormalizedAssetRootPath,
                    1,
                    packageVersion.InstallerPath,
                    PackageSourceKind.DirectFolder,
                    ScratchPolicy.Persistent)
            ];
        }

        return packageVersion.Media.Select(m => new PackageMediaResponse(m.Id, m.MediaType, m.Label, m.Path, m.DiscNumber, m.EntrypointHint, m.SourceKind, m.ScratchPolicy)).ToArray();
    }

    private static bool NormalizedRootHasExecutableContent(string root)
    {
        if (!Directory.Exists(root)) return false;
        // A normalized root that only contains _disc-media is not directly executable —
        // fall back to original media so the agent mounts the ISOs directly.
        var entries = Directory.GetFileSystemEntries(root);
        if (entries.Length == 0) return false;
        if (entries.All(e => string.Equals(Path.GetFileName(e), "_disc-media", StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    private IQueryable<Job> Query() =>
        dbContext.Jobs
            .Include(j => j.Package)
            .Include(j => j.PackageVersion)
            .Include(j => j.Machine)
            .Include(j => j.Events)
            .Include(j => j.Logs)
            .OrderByDescending(j => j.CreatedAtUtc);
}
