using Gamarr.Application.Contracts;
using Gamarr.Application.Exceptions;
using Gamarr.Application.Interfaces;
using Gamarr.Application.Services;
using Gamarr.Domain.Entities;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamarr.Infrastructure.Services;

public sealed class MachineService(GamarrDbContext dbContext) : IMachineService
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(2);

    public async Task<MachineResponse> RegisterAsync(RegisterMachineRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidateMachineRegistration(request);

        var machine = await dbContext.Machines.FirstOrDefaultAsync(x => x.StableKey == request.StableKey, cancellationToken);

        if (machine is null)
        {
            machine = new Machine
            {
                StableKey = request.StableKey.Trim(),
                Name = request.Name.Trim(),
                Hostname = request.Hostname.Trim(),
                OperatingSystem = request.OperatingSystem.Trim(),
                Architecture = request.Architecture,
                AgentVersion = request.AgentVersion.Trim(),
                Status = MachineStatus.Online
            };

            dbContext.Machines.Add(machine);
        }
        else
        {
            machine.Name = request.Name.Trim();
            machine.Hostname = request.Hostname.Trim();
            machine.OperatingSystem = request.OperatingSystem.Trim();
            machine.Architecture = request.Architecture;
            machine.AgentVersion = request.AgentVersion.Trim();
            machine.Status = MachineStatus.Online;
            machine.LastHeartbeatUtc = DateTimeOffset.UtcNow;
        }

        await ReplaceCapabilitiesAsync(machine.Id, request.Capabilities, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(machine.Id, cancellationToken))!;
    }

    public async Task<MachineResponse?> HeartbeatAsync(Guid machineId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidateHeartbeat(request);

        var machine = await dbContext.Machines.FirstOrDefaultAsync(x => x.Id == machineId, cancellationToken);

        if (machine is null)
        {
            return null;
        }

        machine.Status = request.Status;
        machine.AgentVersion = request.AgentVersion.Trim();
        machine.LastHeartbeatUtc = DateTimeOffset.UtcNow;
        await ReplaceCapabilitiesAsync(machine.Id, request.Capabilities, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(machine.Id, cancellationToken))!;
    }

    public async Task<IReadOnlyCollection<MachineResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var machines = await dbContext.Machines.Include(x => x.Capabilities).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var activeJobMachineIds = await dbContext.Jobs
            .Where(x => x.State != JobState.Completed && x.State != JobState.Failed && x.State != JobState.Cancelled)
            .Select(x => x.MachineId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var machineIdsWithAnyJobs = await dbContext.Jobs
            .Select(x => x.MachineId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return machines.Select(m => ToResponse(m, activeJobMachineIds.Contains(m.Id), machineIdsWithAnyJobs.Contains(m.Id))).ToArray();
    }

    public async Task<MachineResponse?> GetAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machine = await dbContext.Machines.Include(x => x.Capabilities)
            .FirstOrDefaultAsync(x => x.Id == machineId, cancellationToken);
        if (machine is null)
        {
            return null;
        }

        var hasActiveJobs = await dbContext.Jobs.AnyAsync(
            x => x.MachineId == machineId &&
                 x.State != JobState.Completed &&
                 x.State != JobState.Failed &&
                 x.State != JobState.Cancelled,
            cancellationToken);
        var hasAnyJobs = hasActiveJobs || await dbContext.Jobs.AnyAsync(x => x.MachineId == machineId, cancellationToken);
        return ToResponse(machine, hasActiveJobs, hasAnyJobs);
    }

    public async Task RemoveAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machine = await dbContext.Machines.FirstOrDefaultAsync(x => x.Id == machineId, cancellationToken)
            ?? throw new AppNotFoundException("Machine not found.");

        var hasActiveJobs = await dbContext.Jobs.AnyAsync(
            x => x.MachineId == machineId &&
                 x.State != JobState.Completed &&
                 x.State != JobState.Failed &&
                 x.State != JobState.Cancelled,
            cancellationToken);

        if (hasActiveJobs)
        {
            throw new AppConflictException("Machine cannot be removed while it has active jobs.");
        }

        if (!IsStale(machine))
        {
            throw new AppConflictException("Only stale or offline machine records can be removed.");
        }

        var hasAnyJobs = await dbContext.Jobs.AnyAsync(x => x.MachineId == machineId, cancellationToken);
        if (hasAnyJobs)
        {
            throw new AppConflictException("Machine cannot be removed because it still has job history.");
        }

        await dbContext.MachinePackageInstalls.Where(x => x.MachineId == machineId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.MachineMounts.Where(x => x.MachineId == machineId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.MachineCapabilities.Where(x => x.MachineId == machineId).ExecuteDeleteAsync(cancellationToken);
        dbContext.Machines.Remove(machine);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ReplaceCapabilitiesAsync(Guid machineId, IEnumerable<string> capabilities, CancellationToken cancellationToken)
    {
        await dbContext.MachineCapabilities.Where(x => x.MachineId == machineId).ExecuteDeleteAsync(cancellationToken);

        foreach (var capability in capabilities.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            dbContext.MachineCapabilities.Add(new MachineCapability
            {
                MachineId = machineId,
                Capability = capability.Trim()
            });
        }
    }

    private static MachineResponse ToResponse(Machine machine, bool hasActiveJobs, bool hasAnyJobs)
    {
        var isStale = IsStale(machine);
        var canRemove = isStale && !hasActiveJobs && !hasAnyJobs;
        var blockedReason = canRemove
            ? null
            : hasActiveJobs
                ? "Machine has active jobs."
                : hasAnyJobs
                    ? "Machine has job history."
                    : !isStale
                        ? "Machine is still active."
                        : "Machine cannot be removed.";

        return new MachineResponse(
            machine.Id,
            machine.StableKey,
            machine.Name,
            machine.Hostname,
            machine.OperatingSystem,
            machine.Architecture,
            machine.AgentVersion,
            machine.Status,
            machine.RegisteredAtUtc,
            machine.LastHeartbeatUtc,
            machine.Capabilities.Select(c => c.Capability).OrderBy(c => c).ToArray(),
            isStale,
            canRemove,
            blockedReason,
            hasActiveJobs);
    }

    private static bool IsStale(Machine machine)
        => machine.Status == MachineStatus.Offline || DateTimeOffset.UtcNow - machine.LastHeartbeatUtc > StaleThreshold;
}
