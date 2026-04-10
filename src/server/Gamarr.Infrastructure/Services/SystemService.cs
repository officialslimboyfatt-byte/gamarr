using System.Security.Cryptography;
using System.Text;
using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Gamarr.Domain.Entities;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamarr.Infrastructure.Services;

public sealed class SystemService(GamarrDbContext dbContext, ISettingsService settingsService) : ISystemService
{
    private const int MaxEventBatch = 250;
    private const int MaxLogBatch = 250;
    private const int MaxTailLines = 400;

    public async Task<SystemHealthResponse> GetHealthAsync(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var staleThreshold = now.AddMinutes(-2);

        var machines = await dbContext.Machines
            .Include(x => x.Capabilities)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var settings = await settingsService.GetAsync(cancellationToken);
        var network = settings.Network;
        var roots = await dbContext.LibraryRoots.AsNoTracking().ToListAsync(cancellationToken);
        var activeJobs = await dbContext.Jobs.CountAsync(
            x => x.State != JobState.Completed && x.State != JobState.Failed && x.State != JobState.Cancelled,
            cancellationToken);
        var failedJobsLast24Hours = await dbContext.Jobs.CountAsync(
            x => x.State == JobState.Failed && (x.CompletedAtUtc ?? x.CreatedAtUtc) >= now.AddHours(-24),
            cancellationToken);
        var runningScans = await dbContext.LibraryScans.CountAsync(x => x.State == LibraryScanState.Running, cancellationToken);
        var pendingNormalizationJobs = await dbContext.NormalizationJobs.CountAsync(
            x => x.State != "Completed" && x.State != "Failed",
            cancellationToken);
        var failedMounts = await dbContext.MachineMounts.CountAsync(x => x.Status == "Failed", cancellationToken);

        var onlineMachines = machines.Count(x =>
            x.Status is MachineStatus.Online or MachineStatus.Busy &&
            x.LastHeartbeatUtc >= staleThreshold);
        var staleMachines = machines.Count(x => x.LastHeartbeatUtc < staleThreshold);
        var missingWinCDEmu = machines.Count(x => !HasCapability(x, "wincdemu"));
        var missing7Zip = machines.Count(x => !HasCapability(x, "7zip"));
        var failedScans = await dbContext.LibraryScans.CountAsync(
            x => x.State == LibraryScanState.Failed && x.StartedAtUtc >= now.AddDays(-7),
            cancellationToken);
        var failedNormalizationJobs = await dbContext.NormalizationJobs.CountAsync(
            x => x.State == "Failed" && x.CreatedAtUtc >= now.AddDays(-7),
            cancellationToken);

        var checks = new List<SystemHealthCheckResponse>
        {
            new(
                "database",
                "Database",
                canConnect ? "Healthy" : "Error",
                canConnect ? "Postgres is reachable." : "Database connectivity failed. The API is not ready.",
                null),
            new(
                "machines",
                "Machines",
                onlineMachines > 0 ? (staleMachines > 0 ? "Warning" : "Healthy") : "Error",
                onlineMachines > 0
                    ? $"{onlineMachines} machine(s) online. {staleMachines} stale heartbeat(s)."
                    : "No online machines are currently reporting.",
                "/machines"),
            new(
                "capabilities",
                "Machine Capabilities",
                missingWinCDEmu > 0 || missing7Zip > 0 ? "Warning" : "Healthy",
                missingWinCDEmu > 0 || missing7Zip > 0
                    ? $"{missingWinCDEmu} machine(s) missing WinCDEmu, {missing7Zip} missing 7-Zip."
                    : "All registered machines report core media capabilities.",
                "/machines"),
            new(
                "queue",
                "Queue",
                failedJobsLast24Hours > 0 ? "Warning" : "Healthy",
                $"{activeJobs} active job(s). {failedJobsLast24Hours} failed job(s) in the last 24 hours.",
                "/system/queue"),
            new(
                "library",
                "Library Operations",
                failedScans > 0 || failedNormalizationJobs > 0 ? "Warning" : "Healthy",
                $"{runningScans} scan(s) running. {pendingNormalizationJobs} normalization job(s) pending. {failedScans} scan failure(s), {failedNormalizationJobs} normalization failure(s) in the last 7 days.",
                "/imports"),
            new(
                "mounts",
                "Mounting",
                failedMounts > 0 ? "Warning" : "Healthy",
                failedMounts > 0 ? $"{failedMounts} mount command(s) failed." : "No failed mount commands are recorded.",
                "/machines"),
            new(
                "configuration",
                "Configuration",
                GetConfigurationStatus(settings, roots),
                BuildConfigurationSummary(settings, roots),
                "/settings"),
            new(
                "network",
                "Network Access",
                network.LanEnabled ? "Healthy" : "Warning",
                network.Summary,
                "/settings")
        };

        var overallStatus = checks.Any(x => x.Status == "Error")
            ? "Error"
            : checks.Any(x => x.Status == "Warning")
                ? "Warning"
                : "Healthy";

        return new SystemHealthResponse(
            now,
            overallStatus,
            overallStatus switch
            {
                "Error" => "Critical system issues need attention.",
                "Warning" => "The system is operating with warnings.",
                _ => "All core system checks are healthy."
            },
            new SystemHealthMetricsResponse(
                machines.Count,
                onlineMachines,
                activeJobs,
                failedJobsLast24Hours,
                runningScans,
                pendingNormalizationJobs,
                failedMounts),
            checks);
    }

    public async Task<IReadOnlyCollection<SystemEventResponse>> ListEventsAsync(string? category, string? severity, Guid? machineId, string? search, int limit, CancellationToken cancellationToken)
    {
        var targetLimit = Math.Clamp(limit, 1, MaxEventBatch);
        var events = new List<SystemEventResponse>(targetLimit * 2);

        var jobEvents = await dbContext.JobEvents
            .Include(x => x.Job!)
                .ThenInclude(x => x.Package)
            .Include(x => x.Job!)
                .ThenInclude(x => x.Machine)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(MaxEventBatch)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        events.AddRange(jobEvents.Select(x => new SystemEventResponse(
            $"job-event:{x.Id}",
            x.CreatedAtUtc,
            "Job",
            x.State switch
            {
                JobState.Failed => "Error",
                JobState.Cancelled => "Warning",
                JobState.Completed => "Info",
                _ => "Info"
            },
            $"{x.Job?.ActionType} {x.State}",
            x.Message,
            x.JobId,
            x.Job?.PackageId,
            x.Job?.Package?.Name,
            x.Job?.MachineId,
            x.Job?.Machine?.Name,
            x.JobId != Guid.Empty ? $"/jobs/{x.JobId}" : null)));

        var scans = await dbContext.LibraryScans
            .Include(x => x.LibraryRoot)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(80)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        events.AddRange(scans.Select(x => new SystemEventResponse(
            $"scan:{x.Id}",
            x.CompletedAtUtc ?? x.StartedAtUtc,
            "Scan",
            x.State == LibraryScanState.Failed ? "Error" : x.State == LibraryScanState.Running ? "Info" : "Info",
            $"Scan {x.State}",
            $"{x.LibraryRoot?.DisplayName ?? "Library root"}: {x.Summary}",
            null,
            null,
            x.LibraryRoot?.DisplayName,
            null,
            null,
            "/imports")));

        var normalizationJobs = await dbContext.NormalizationJobs
            .Include(x => x.Package)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(80)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        events.AddRange(normalizationJobs.Select(x => new SystemEventResponse(
            $"normalization:{x.Id}",
            x.CompletedAtUtc ?? x.UpdatedAtUtc,
            "Normalization",
            string.Equals(x.State, "Failed", StringComparison.OrdinalIgnoreCase) ? "Error" : "Info",
            $"Normalization {x.State}",
            $"{x.Package?.Name ?? "Package"}: {x.Summary}",
            null,
            x.PackageId,
            x.Package?.Name,
            null,
            null,
            x.PackageId != Guid.Empty ? $"/packages/{x.PackageId}" : "/packages")));

        var mounts = await dbContext.MachineMounts
            .Include(x => x.Machine)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(80)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        events.AddRange(mounts.Select(x => new SystemEventResponse(
            $"mount:{x.Id}",
            x.CompletedAtUtc ?? x.CreatedAtUtc,
            "Mount",
            x.Status == "Failed" ? "Error" : x.Status == "Mounted" ? "Info" : "Warning",
            $"Mount {x.Status}",
            $"{x.Machine?.Name ?? "Machine"}: {x.IsoPath}",
            null,
            null,
            null,
            x.MachineId,
            x.Machine?.Name,
            x.MachineId != Guid.Empty ? $"/machines/{x.MachineId}" : "/machines")));

        IEnumerable<SystemEventResponse> query = events.OrderByDescending(x => x.CreatedAtUtc);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => x.Category.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(x => x.Severity.Equals(severity.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (machineId.HasValue)
        {
            query = query.Where(x => x.MachineId == machineId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                ContainsIgnoreCase(x.Title, term) ||
                ContainsIgnoreCase(x.Message, term) ||
                ContainsIgnoreCase(x.PackageName, term) ||
                ContainsIgnoreCase(x.MachineName, term));
        }

        return query.Take(targetLimit).ToArray();
    }

    public async Task<IReadOnlyCollection<SystemLogResponse>> ListStructuredLogsAsync(string? level, string? source, Guid? machineId, string? search, int limit, CancellationToken cancellationToken)
    {
        var targetLimit = Math.Clamp(limit, 1, MaxLogBatch);
        var query = dbContext.PackageActionLogs
            .Include(x => x.Job!)
                .ThenInclude(x => x.Package)
            .Include(x => x.Job!)
                .ThenInclude(x => x.Machine)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(level) && Enum.TryParse<LogLevelKind>(level, true, out var parsedLevel))
        {
            query = query.Where(x => x.Level == parsedLevel);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var trimmedSource = source.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Source, trimmedSource));
        }

        if (machineId.HasValue)
        {
            query = query.Where(x => x.Job != null && x.Job.MachineId == machineId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Message, term) ||
                (x.PayloadJson != null && EF.Functions.ILike(x.PayloadJson, term)) ||
                (x.Job != null && x.Job.Package != null && EF.Functions.ILike(x.Job.Package.Name, term)) ||
                (x.Job != null && x.Job.Machine != null && EF.Functions.ILike(x.Job.Machine.Name, term)));
        }

        var logs = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(targetLimit)
            .ToListAsync(cancellationToken);

        return logs.Select(x => new SystemLogResponse(
            x.Id,
            x.CreatedAtUtc,
            x.Level,
            x.Source,
            x.Message,
            x.PayloadJson,
            x.JobId,
            x.Job?.Package?.Name,
            x.Job?.Machine?.Name,
            $"/jobs/{x.JobId}"))
            .ToArray();
    }

    public Task<IReadOnlyCollection<SystemLogFileResponse>> ListLogFilesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = DiscoverLogFiles()
            .Select(x => new SystemLogFileResponse(x.Id, x.Name, x.DisplayName, x.Info.Length, x.Info.LastWriteTimeUtc))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<SystemLogFileResponse>>(files);
    }

    public async Task<SystemLogFileContentResponse?> GetLogFileAsync(string id, int tailLines, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = DiscoverLogFiles().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(descriptor.Info.FullName, cancellationToken);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var takeLines = Math.Clamp(tailLines, 20, MaxTailLines);
        var truncated = lines.Length > takeLines;
        var selected = truncated ? lines[^takeLines..] : lines;

        return new SystemLogFileContentResponse(
            descriptor.Id,
            descriptor.Name,
            descriptor.DisplayName,
            descriptor.Info.Length,
            descriptor.Info.LastWriteTimeUtc,
            truncated,
            string.Join(Environment.NewLine, selected));
    }

    private static bool HasCapability(Machine machine, string capability)
        => machine.Capabilities.Any(x => x.Capability.Equals(capability, StringComparison.OrdinalIgnoreCase));

    private static string GetConfigurationStatus(SettingsResponse settings, IReadOnlyCollection<LibraryRoot> roots)
    {
        if (roots.Count == 0 || string.IsNullOrWhiteSpace(settings.Media.DefaultLibraryRootPath))
        {
            return "Warning";
        }

        if (!settings.Metadata.IgdbConfigured)
        {
            return "Warning";
        }

        return "Healthy";
    }

    private static string BuildConfigurationSummary(SettingsResponse settings, IReadOnlyCollection<LibraryRoot> roots)
    {
        var warnings = new List<string>();

        if (roots.Count == 0)
        {
            warnings.Add("No library roots are configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.Media.DefaultLibraryRootPath))
        {
            warnings.Add("Default library root path is empty.");
        }

        if (!settings.Metadata.IgdbConfigured)
        {
            warnings.Add("IGDB is not fully configured.");
        }

        return warnings.Count == 0
            ? "Core library and metadata settings are configured."
            : string.Join(" ", warnings);
    }

    private static bool ContainsIgnoreCase(string? value, string term)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyCollection<LogFileDescriptor> DiscoverLogFiles()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return [];
        }

        var logDirectory = Path.Combine(root, ".runtime-tests");
        if (!Directory.Exists(logDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info =>
            {
                var name = info.Name;
                return new LogFileDescriptor(
                    BuildLogFileId(info.FullName),
                    name,
                    $"Runtime / {name}",
                    info);
            })
            .ToArray();
    }

    private static string? FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var hasWeb = Directory.Exists(Path.Combine(current.FullName, "src", "web"));
                var hasServer = Directory.Exists(Path.Combine(current.FullName, "src", "server"));
                if (hasWeb && hasServer)
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static string BuildLogFileId(string fullPath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        return Convert.ToHexString(bytes[..8]);
    }

    private sealed record LogFileDescriptor(string Id, string Name, string DisplayName, FileInfo Info);
}
