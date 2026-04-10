using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/agents")]
public sealed class AgentsController(IJobService jobService) : ControllerBase
{
    [HttpPost("{machineId:guid}/next-job")]
    public async Task<ActionResult<NextJobResponse>> NextJob(Guid machineId, CancellationToken cancellationToken)
    {
        var job = await jobService.GetNextJobAsync(machineId, cancellationToken);
        return job is null ? NoContent() : Ok(job);
    }
}
