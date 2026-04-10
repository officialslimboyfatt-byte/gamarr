using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController(ISystemService systemService) : ControllerBase
{
    [HttpGet("health")]
    public async Task<ActionResult<SystemHealthResponse>> GetHealth(CancellationToken cancellationToken)
        => Ok(await systemService.GetHealthAsync(cancellationToken));

    [HttpGet("events")]
    public async Task<ActionResult<IReadOnlyCollection<SystemEventResponse>>> ListEvents(
        [FromQuery] string? category,
        [FromQuery] string? severity,
        [FromQuery] Guid? machineId,
        [FromQuery] string? search,
        [FromQuery] int limit = 150,
        CancellationToken cancellationToken = default)
        => Ok(await systemService.ListEventsAsync(category, severity, machineId, search, limit, cancellationToken));

    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyCollection<SystemLogResponse>>> ListStructuredLogs(
        [FromQuery] string? level,
        [FromQuery] string? source,
        [FromQuery] Guid? machineId,
        [FromQuery] string? search,
        [FromQuery] int limit = 150,
        CancellationToken cancellationToken = default)
        => Ok(await systemService.ListStructuredLogsAsync(level, source, machineId, search, limit, cancellationToken));

    [HttpGet("log-files")]
    public async Task<ActionResult<IReadOnlyCollection<SystemLogFileResponse>>> ListLogFiles(CancellationToken cancellationToken)
        => Ok(await systemService.ListLogFilesAsync(cancellationToken));

    [HttpGet("log-files/{id}")]
    public async Task<ActionResult<SystemLogFileContentResponse>> GetLogFile(string id, [FromQuery] int tailLines = 200, CancellationToken cancellationToken = default)
    {
        var file = await systemService.GetLogFileAsync(id, tailLines, cancellationToken);
        return file is null ? NotFound() : Ok(file);
    }
}
