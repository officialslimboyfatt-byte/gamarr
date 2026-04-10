using Gamarr.Domain.Enums;

namespace Gamarr.Application.Contracts;

public sealed record RegisterMachineRequest(
    string StableKey,
    string Name,
    string Hostname,
    string OperatingSystem,
    ArchitectureKind Architecture,
    string AgentVersion,
    IReadOnlyCollection<string> Capabilities);

public sealed record HeartbeatRequest(
    MachineStatus Status,
    string AgentVersion,
    IReadOnlyCollection<string> Capabilities);

public sealed record MachineResponse(
    Guid Id,
    string StableKey,
    string Name,
    string Hostname,
    string OperatingSystem,
    ArchitectureKind Architecture,
    string AgentVersion,
    MachineStatus Status,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastHeartbeatUtc,
    IReadOnlyCollection<string> Capabilities,
    bool IsStale,
    bool CanRemove,
    string? RemoveBlockedReason,
    bool HasActiveJobs);

public sealed record CreateMountRequest(string IsoPath);

public sealed record MachineMountResponse(
    Guid Id,
    Guid MachineId,
    string IsoPath,
    string Status,
    string? DriveLetter,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record NextMountResponse(Guid MountId, string IsoPath);

public sealed record ReportMountResultRequest(string? DriveLetter, string? Error);

public sealed record QuickInstallRequest(string IsoPath, Guid MachineId, string? Label);

public sealed record QuickInstallResponse(Guid JobId, Guid PackageId);
