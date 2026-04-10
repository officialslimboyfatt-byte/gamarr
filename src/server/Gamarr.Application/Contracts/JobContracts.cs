using Gamarr.Domain.Enums;

namespace Gamarr.Application.Contracts;

public sealed record CreateJobRequest(
    Guid PackageId,
    Guid MachineId,
    JobActionType ActionType,
    string RequestedBy);

public sealed record ClaimJobRequest(Guid MachineId);

public sealed record JobEventRequest(
    int SequenceNumber,
    JobState State,
    string Message,
    IReadOnlyCollection<JobLogRequest> Logs,
    string? LaunchExecutablePath = null,
    bool? DetectedInstalled = null,
    string? ValidationMessage = null,
    string? InstallLocation = null,
    string? ResolvedUninstallCommand = null);

public sealed record JobLogRequest(
    LogLevelKind Level,
    string Source,
    string Message,
    string? PayloadJson);

public sealed record JobResponse(
    Guid Id,
    Guid PackageId,
    Guid PackageVersionId,
    Guid MachineId,
    string PackageName,
    string PackageVersionLabel,
    string MachineName,
    JobActionType ActionType,
    JobState State,
    string RequestedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    double? DurationSeconds,
    string? LatestEventMessage,
    string? OutcomeSummary,
    IReadOnlyCollection<JobEventResponse> Events,
    IReadOnlyCollection<JobLogResponse> Logs);

public sealed record JobEventResponse(
    Guid Id,
    int SequenceNumber,
    JobState State,
    string Message,
    DateTimeOffset CreatedAtUtc);

public sealed record JobLogResponse(
    Guid Id,
    LogLevelKind Level,
    string Source,
    string Message,
    string? PayloadJson,
    DateTimeOffset CreatedAtUtc);

public sealed record NextJobResponse(
    Guid Id,
    Guid PackageId,
    Guid PackageVersionId,
    JobActionType ActionType,
    string PackageName,
    string PackageVersionLabel,
    string SupportedOs,
    ArchitectureKind Architecture,
    InstallScriptKind InstallScriptKind,
    string InstallScriptPath,
    string? UninstallScriptPath,
    string? UninstallArguments,
    string ManifestFormatVersion,
    string ManifestJson,
    int TimeoutSeconds,
    string Notes,
    string InstallStrategy,
    string InstallerFamily,
    string? InstallerPath,
    string? SilentArguments,
    string InstallDiagnostics,
    string? LaunchExecutablePath,
    IReadOnlyCollection<PackageMediaResponse> Media,
    IReadOnlyCollection<DetectionRuleResponse> DetectionRules,
    IReadOnlyCollection<PrerequisiteResponse> Prerequisites);
