using Gamarr.Domain.Enums;

namespace Gamarr.Application.Contracts;

public sealed record SystemHealthResponse(
    DateTimeOffset GeneratedAtUtc,
    string OverallStatus,
    string Summary,
    SystemHealthMetricsResponse Metrics,
    IReadOnlyCollection<SystemHealthCheckResponse> Checks);

public sealed record SystemHealthMetricsResponse(
    int TotalMachines,
    int OnlineMachines,
    int ActiveJobs,
    int FailedJobsLast24Hours,
    int RunningScans,
    int PendingNormalizationJobs,
    int FailedMounts);

public sealed record SystemHealthCheckResponse(
    string Key,
    string Name,
    string Status,
    string Summary,
    string? ActionPath);

public sealed record SystemEventResponse(
    string Id,
    DateTimeOffset CreatedAtUtc,
    string Category,
    string Severity,
    string Title,
    string Message,
    Guid? JobId,
    Guid? PackageId,
    string? PackageName,
    Guid? MachineId,
    string? MachineName,
    string? ActionPath);

public sealed record SystemLogResponse(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    LogLevelKind Level,
    string Source,
    string Message,
    string? PayloadJson,
    Guid JobId,
    string? PackageName,
    string? MachineName,
    string? ActionPath);

public sealed record SystemLogFileResponse(
    string Id,
    string Name,
    string DisplayName,
    long SizeBytes,
    DateTimeOffset UpdatedAtUtc);

public sealed record SystemLogFileContentResponse(
    string Id,
    string Name,
    string DisplayName,
    long SizeBytes,
    DateTimeOffset UpdatedAtUtc,
    bool Truncated,
    string Content);
