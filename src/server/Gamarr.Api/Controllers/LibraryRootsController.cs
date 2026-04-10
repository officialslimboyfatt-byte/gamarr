using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/library-roots")]
public sealed class LibraryRootsController(ILibraryService libraryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<LibraryRootResponse>>> List(CancellationToken cancellationToken)
        => Ok(await libraryService.ListRootsAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<LibraryRootResponse>> Create([FromBody] CreateLibraryRootRequest request, CancellationToken cancellationToken)
    {
        var created = await libraryService.CreateRootAsync(request, cancellationToken);
        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/scan")]
    public async Task<ActionResult<LibraryScanResponse>> Scan(Guid id, CancellationToken cancellationToken)
        => Ok(await libraryService.ScanRootAsync(id, cancellationToken));
}
