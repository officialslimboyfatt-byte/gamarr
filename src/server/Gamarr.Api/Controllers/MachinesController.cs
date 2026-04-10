using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Gamarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/machines")]
public sealed class MachinesController(IMachineService machineService, MountCommandService mountService, MachinePrerequisiteService prerequisiteService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<MachineResponse>> Register([FromBody] RegisterMachineRequest request, CancellationToken cancellationToken)
    {
        var machine = await machineService.RegisterAsync(request, cancellationToken);
        return Ok(machine);
    }

    [HttpPost("{id:guid}/heartbeat")]
    public async Task<ActionResult<MachineResponse>> Heartbeat(Guid id, [FromBody] HeartbeatRequest request, CancellationToken cancellationToken)
    {
        var machine = await machineService.HeartbeatAsync(id, request, cancellationToken);
        return machine is null ? NotFound() : Ok(machine);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<MachineResponse>>> List(CancellationToken cancellationToken)
        => Ok(await machineService.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MachineResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var machine = await machineService.GetAsync(id, cancellationToken);
        return machine is null ? NotFound() : Ok(machine);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await machineService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/install-wincdemu")]
    public async Task<ActionResult<JobResponse>> InstallWinCDEmu(Guid id, CancellationToken cancellationToken)
        => Ok(await prerequisiteService.QueueWinCDEmuInstallAsync(id, cancellationToken));

    // --- Image mount endpoints ---

    [HttpGet("{id:guid}/mounts")]
    public async Task<ActionResult<IReadOnlyCollection<MachineMountResponse>>> ListMounts(Guid id, CancellationToken cancellationToken)
        => Ok(await mountService.ListMountsAsync(id, cancellationToken));

    [HttpPost("{id:guid}/mounts")]
    public async Task<ActionResult<MachineMountResponse>> CreateMount(Guid id, [FromBody] CreateMountRequest request, CancellationToken cancellationToken)
    {
        var mount = await mountService.CreateMountAsync(id, request, cancellationToken);
        return Ok(mount);
    }

    [HttpGet("{id:guid}/mounts/{mountId:guid}")]
    public async Task<ActionResult<MachineMountResponse>> GetMount(Guid id, Guid mountId, CancellationToken cancellationToken)
    {
        var mount = await mountService.GetMountAsync(id, mountId, cancellationToken);
        return mount is null ? NotFound() : Ok(mount);
    }

    [HttpPost("{id:guid}/mounts/{mountId:guid}/dismount")]
    public async Task<ActionResult<MachineMountResponse>> RequestDismount(Guid id, Guid mountId, CancellationToken cancellationToken)
    {
        var mount = await mountService.RequestDismountAsync(id, mountId, cancellationToken);
        return mount is null ? NotFound() : Ok(mount);
    }

    // --- Agent polling endpoints ---

    [HttpGet("{id:guid}/next-mount")]
    public async Task<ActionResult<NextMountResponse>> GetNextMount(Guid id, CancellationToken cancellationToken)
    {
        var mount = await mountService.GetNextPendingMountAsync(id, cancellationToken);
        return mount is null ? NoContent() : Ok(mount);
    }

    [HttpGet("{id:guid}/next-dismount")]
    public async Task<ActionResult<NextMountResponse>> GetNextDismount(Guid id, CancellationToken cancellationToken)
    {
        var mount = await mountService.GetNextPendingDismountAsync(id, cancellationToken);
        return mount is null ? NoContent() : Ok(mount);
    }

    [HttpPost("{id:guid}/mounts/{mountId:guid}/result")]
    public async Task<ActionResult<MachineMountResponse>> ReportMountResult(Guid id, Guid mountId, [FromBody] ReportMountResultRequest request, CancellationToken cancellationToken)
    {
        var mount = await mountService.ReportMountResultAsync(id, mountId, request, cancellationToken);
        return mount is null ? NotFound() : Ok(mount);
    }

    [HttpPost("{id:guid}/mounts/{mountId:guid}/dismounted")]
    public async Task<ActionResult<MachineMountResponse>> ConfirmDismount(Guid id, Guid mountId, CancellationToken cancellationToken)
    {
        var mount = await mountService.ConfirmDismountAsync(id, mountId, cancellationToken);
        return mount is null ? NotFound() : Ok(mount);
    }
}
