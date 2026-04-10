using Gamarr.Application.Contracts;
using Gamarr.Application.Exceptions;
using Gamarr.Domain.Entities;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamarr.Infrastructure.Services;

public sealed class MountCommandService(GamarrDbContext dbContext)
{
    public async Task<IReadOnlyCollection<MachineMountResponse>> ListMountsAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machineExists = await dbContext.Machines.AnyAsync(m => m.Id == machineId, cancellationToken);
        if (!machineExists)
        {
            throw new AppNotFoundException("Machine not found.");
        }

        var mounts = await dbContext.MachineMounts
            .Where(m => m.MachineId == machineId)
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        return mounts.Select(m => m.ToResponse()).ToArray();
    }

    public async Task<MachineMountResponse> CreateMountAsync(Guid machineId, CreateMountRequest request, CancellationToken cancellationToken)
    {
        var machine = await dbContext.Machines.FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken)
            ?? throw new AppNotFoundException("Machine not found.");

        var mount = new MachineMount
        {
            MachineId = machine.Id,
            IsoPath = request.IsoPath.Trim(),
            Status = "Pending",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.MachineMounts.Add(mount);
        await dbContext.SaveChangesAsync(cancellationToken);
        return mount.ToResponse();
    }

    public async Task<MachineMountResponse?> GetMountAsync(Guid machineId, Guid mountId, CancellationToken cancellationToken)
    {
        var mount = await dbContext.MachineMounts
            .FirstOrDefaultAsync(m => m.Id == mountId && m.MachineId == machineId, cancellationToken);
        return mount?.ToResponse();
    }

    public async Task<MachineMountResponse?> RequestDismountAsync(Guid machineId, Guid mountId, CancellationToken cancellationToken)
    {
        var mount = await dbContext.MachineMounts
            .FirstOrDefaultAsync(m => m.Id == mountId && m.MachineId == machineId, cancellationToken);

        if (mount is null) return null;
        if (mount.Status != "Mounted") return mount.ToResponse();

        mount.Status = "DismountRequested";
        await dbContext.SaveChangesAsync(cancellationToken);
        return mount.ToResponse();
    }

    public async Task<NextMountResponse?> GetNextPendingMountAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var mount = await dbContext.MachineMounts
            .Where(m => m.MachineId == machineId && m.Status == "Pending")
            .OrderBy(m => m.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return mount is null ? null : new NextMountResponse(mount.Id, mount.IsoPath);
    }

    public async Task<NextMountResponse?> GetNextPendingDismountAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var mount = await dbContext.MachineMounts
            .Where(m => m.MachineId == machineId && m.Status == "DismountRequested")
            .OrderBy(m => m.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return mount is null ? null : new NextMountResponse(mount.Id, mount.IsoPath);
    }

    public async Task<MachineMountResponse?> ReportMountResultAsync(Guid machineId, Guid mountId, ReportMountResultRequest request, CancellationToken cancellationToken)
    {
        var mount = await dbContext.MachineMounts
            .FirstOrDefaultAsync(m => m.Id == mountId && m.MachineId == machineId, cancellationToken);

        if (mount is null) return null;

        if (request.DriveLetter is not null)
        {
            mount.Status = "Mounted";
            mount.DriveLetter = request.DriveLetter.Trim();
        }
        else
        {
            mount.Status = "Failed";
            mount.ErrorMessage = request.Error;
            mount.CompletedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return mount.ToResponse();
    }

    public async Task<MachineMountResponse?> ConfirmDismountAsync(Guid machineId, Guid mountId, CancellationToken cancellationToken)
    {
        var mount = await dbContext.MachineMounts
            .FirstOrDefaultAsync(m => m.Id == mountId && m.MachineId == machineId, cancellationToken);

        if (mount is null) return null;

        mount.Status = "Dismounted";
        mount.CompletedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return mount.ToResponse();
    }
}

file static class MachineMountExtensions
{
    public static MachineMountResponse ToResponse(this MachineMount m) => new(
        m.Id, m.MachineId, m.IsoPath, m.Status, m.DriveLetter, m.ErrorMessage, m.CreatedAtUtc, m.CompletedAtUtc);
}
