using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/library")]
public sealed class LibraryController(ILibraryService libraryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<LibraryTitleResponse>>> List(
        [FromQuery] Guid? machineId,
        [FromQuery] string? genre,
        [FromQuery] string? studio,
        [FromQuery] int? year,
        [FromQuery] string? sortBy,
        CancellationToken cancellationToken)
        => Ok(await libraryService.ListAsync(machineId, genre, studio, year, sortBy, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LibraryTitleDetailResponse>> Get(Guid id, [FromQuery] Guid? machineId, CancellationToken cancellationToken)
    {
        var title = await libraryService.GetAsync(id, machineId, cancellationToken);
        return title is null ? NotFound() : Ok(title);
    }

    [HttpPost("{id:guid}/re-match")]
    public async Task<ActionResult<LibraryReconcilePreviewResponse>> ReMatch(Guid id, CancellationToken cancellationToken)
        => Ok(await libraryService.PreviewReconcileAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reconcile")]
    public async Task<ActionResult<LibraryTitleDetailResponse>> Reconcile(Guid id, [FromBody] ApplyLibraryReconcileRequest request, [FromQuery] Guid? machineId, CancellationToken cancellationToken)
        => Ok(await libraryService.ApplyReconcileAsync(id, request, machineId, cancellationToken));

    [HttpPost("{id:guid}/search-metadata")]
    public async Task<ActionResult<ManualMetadataSearchResponse>> SearchMetadata(Guid id, [FromBody] ManualMetadataSearchRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.SearchPackageMetadataAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/apply-metadata-search")]
    public async Task<ActionResult<LibraryTitleDetailResponse>> ApplyMetadataSearch(Guid id, [FromBody] ApplyManualMetadataMatchRequest request, [FromQuery] Guid? machineId, CancellationToken cancellationToken)
        => Ok(await libraryService.ApplyPackageMetadataSearchAsync(id, request, machineId, cancellationToken));

    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<LibraryTitleDetailResponse>> Archive(Guid id, [FromBody] ArchiveLibraryTitleRequest request, [FromQuery] Guid? machineId, CancellationToken cancellationToken)
        => Ok(await libraryService.ArchiveAsync(id, request.Reason, machineId, cancellationToken));

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<LibraryTitleDetailResponse>> Restore(Guid id, [FromQuery] Guid? machineId, CancellationToken cancellationToken)
        => Ok(await libraryService.RestoreAsync(id, machineId, cancellationToken));

    [HttpPost("{id:guid}/play")]
    public async Task<ActionResult<PlayLibraryTitleResponse>> Play(Guid id, [FromBody] PlayLibraryTitleRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.PlayAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/validate-install")]
    public async Task<ActionResult<PlayLibraryTitleResponse>> ValidateInstall(Guid id, [FromBody] LibraryMachineActionRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.ValidateInstallAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/uninstall")]
    public async Task<ActionResult<PlayLibraryTitleResponse>> Uninstall(Guid id, [FromBody] LibraryMachineActionRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.UninstallAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/mark-not-installed")]
    public async Task<ActionResult<LibraryTitleDetailResponse>> MarkNotInstalled(Guid id, [FromBody] LibraryMachineActionRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.MarkNotInstalledAsync(id, request, cancellationToken));

    [HttpPost("reset")]
    public async Task<ActionResult<ResetLibraryResponse>> Reset([FromBody] ResetLibraryRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.ResetAsync(request, cancellationToken));

    [HttpPost("re-match-unmatched")]
    public async Task<ActionResult<BulkReMatchResponse>> BulkReMatch(CancellationToken cancellationToken)
        => Ok(await libraryService.BulkReMatchAsync(cancellationToken));
}
