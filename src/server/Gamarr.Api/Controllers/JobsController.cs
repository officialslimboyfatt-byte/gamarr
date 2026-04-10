using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(IJobService jobService, QuickInstallService quickInstallService) : ControllerBase
{
    [HttpPost("quick-install")]
    public async Task<ActionResult<QuickInstallResponse>> QuickInstall([FromBody] QuickInstallRequest request, CancellationToken cancellationToken)
    {
        var result = await quickInstallService.QuickInstallAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<JobResponse>> Create([FromBody] CreateJobRequest request, CancellationToken cancellationToken)
    {
        var job = await jobService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<JobResponse>>> List(
        [FromQuery] Guid? machineId,
        [FromQuery] JobState? state,
        [FromQuery] JobActionType? actionType,
        [FromQuery] string? search,
        [FromQuery] string? scope,
        CancellationToken cancellationToken)
        => Ok(await jobService.ListAsync(machineId, state, actionType, search, scope, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var job = await jobService.GetAsync(id, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("{id:guid}/claim")]
    public async Task<ActionResult<JobResponse>> Claim(Guid id, [FromBody] ClaimJobRequest request, CancellationToken cancellationToken)
    {
        var job = await jobService.ClaimAsync(id, request, cancellationToken);
        return job is null ? Conflict() : Ok(job);
    }

    [HttpPost("{id:guid}/events")]
    public async Task<ActionResult<JobResponse>> AddEvent(Guid id, [FromBody] JobEventRequest request, CancellationToken cancellationToken)
    {
        var job = await jobService.AddEventAsync(id, request, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<JobResponse>> Cancel(Guid id, CancellationToken cancellationToken)
        => Ok(await jobService.CancelJobAsync(id, cancellationToken));
}
