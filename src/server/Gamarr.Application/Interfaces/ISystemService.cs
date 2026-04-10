using Gamarr.Application.Contracts;

namespace Gamarr.Application.Interfaces;

public interface ISystemService
{
    Task<SystemHealthResponse> GetHealthAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SystemEventResponse>> ListEventsAsync(string? category, string? severity, Guid? machineId, string? search, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SystemLogResponse>> ListStructuredLogsAsync(string? level, string? source, Guid? machineId, string? search, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SystemLogFileResponse>> ListLogFilesAsync(CancellationToken cancellationToken);
    Task<SystemLogFileContentResponse?> GetLogFileAsync(string id, int tailLines, CancellationToken cancellationToken);
}
