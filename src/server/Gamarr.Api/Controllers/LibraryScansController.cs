using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Gamarr.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/library-scans")]
public sealed class LibraryScansController(ILibraryService libraryService) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<LibraryScanResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var scan = await libraryService.GetScanAsync(id, cancellationToken);
        return scan is null ? NotFound() : Ok(scan);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<LibraryScanResponse>>> List(
        [FromQuery] Guid? rootId,
        [FromQuery] LibraryScanState? state,
        CancellationToken cancellationToken)
        => Ok(await libraryService.ListScansAsync(rootId, state, cancellationToken));

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<LibraryScanResponse>> Cancel(Guid id, CancellationToken cancellationToken)
        => Ok(await libraryService.CancelScanAsync(id, cancellationToken));
}
