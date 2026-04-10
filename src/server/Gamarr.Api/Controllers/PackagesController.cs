using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/packages")]
public sealed class PackagesController(IPackageService packageService, INormalizationService normalizationService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PackageResponse>> Create([FromBody] CreatePackageRequest request, CancellationToken cancellationToken)
    {
        var created = await packageService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PackageResponse>>> List(CancellationToken cancellationToken)
        => Ok(await packageService.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PackageResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var package = await packageService.GetAsync(id, cancellationToken);
        return package is null ? NotFound() : Ok(package);
    }

    [HttpPut("{id:guid}/metadata")]
    public async Task<ActionResult<PackageResponse>> UpdateMetadata(Guid id, [FromBody] UpdatePackageMetadataRequest request, CancellationToken cancellationToken) =>
        Ok(await packageService.UpdateMetadataAsync(id, request, cancellationToken));

    [HttpPut("{id:guid}/install-plan")]
    public async Task<ActionResult<PackageResponse>> UpdateInstallPlan(Guid id, [FromBody] UpdatePackageInstallPlanRequest request, CancellationToken cancellationToken) =>
        Ok(await packageService.UpdateInstallPlanAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/normalize")]
    public async Task<ActionResult<PackageResponse>> ReNormalize(Guid id, CancellationToken cancellationToken) =>
        Ok(await packageService.ReNormalizeAsync(id, cancellationToken));

    [HttpPost("normalize-needs-review")]
    public async Task<ActionResult<int>> BulkReNormalize(CancellationToken cancellationToken) =>
        Ok(await packageService.BulkReNormalizeNeedsReviewAsync(cancellationToken));

    [HttpGet("normalization-jobs")]
    public async Task<ActionResult<IReadOnlyCollection<NormalizationJobResponse>>> ListNormalizationJobs(
        [FromQuery] Guid? packageId,
        [FromQuery] string? state,
        CancellationToken cancellationToken)
        => Ok(await normalizationService.ListJobsAsync(packageId, state, cancellationToken));
}
