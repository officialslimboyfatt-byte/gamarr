using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Gamarr.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/library-candidates")]
public sealed class LibraryCandidatesController(ILibraryService libraryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<LibraryCandidateResponse>>> List(
        [FromQuery] LibraryCandidateStatus? status,
        [FromQuery] Guid? rootId,
        [FromQuery] Guid? scanId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
        => Ok(await libraryService.ListCandidatesAsync(status, rootId, scanId, search, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<LibraryCandidateResponse>> Approve(Guid id, CancellationToken cancellationToken)
        => Ok(await libraryService.ApproveCandidateAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<LibraryCandidateResponse>> Reject(Guid id, CancellationToken cancellationToken)
        => Ok(await libraryService.RejectCandidateAsync(id, cancellationToken));

    [HttpPost("{id:guid}/merge")]
    public async Task<ActionResult<LibraryCandidateResponse>> Merge(Guid id, [FromBody] MergeLibraryCandidateRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.MergeCandidateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/unmerge")]
    public async Task<ActionResult<LibraryCandidateResponse>> Unmerge(Guid id, CancellationToken cancellationToken)
        => Ok(await libraryService.UnmergeCandidateAsync(id, cancellationToken));

    [HttpPost("{id:guid}/replace-merge-target")]
    public async Task<ActionResult<LibraryCandidateResponse>> ReplaceMergeTarget(Guid id, [FromBody] ReplaceMergeTargetRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.ReplaceMergeTargetAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/select-match")]
    public async Task<ActionResult<LibraryCandidateResponse>> SelectMatch(Guid id, [FromBody] SelectLibraryCandidateMatchRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.SelectCandidateMatchAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/search-metadata")]
    public async Task<ActionResult<ManualMetadataSearchResponse>> SearchMetadata(Guid id, [FromBody] ManualMetadataSearchRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.SearchCandidateMetadataAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/apply-metadata-search")]
    public async Task<ActionResult<LibraryCandidateResponse>> ApplyMetadataSearch(Guid id, [FromBody] ApplyManualMetadataMatchRequest request, CancellationToken cancellationToken)
        => Ok(await libraryService.ApplyCandidateMetadataSearchAsync(id, request, cancellationToken));
}
