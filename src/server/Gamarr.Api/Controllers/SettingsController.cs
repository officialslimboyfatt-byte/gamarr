using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Gamarr.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(ISettingsService settingsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SettingsResponse>> Get(CancellationToken cancellationToken)
        => Ok(await settingsService.GetAsync(cancellationToken));

    [HttpGet("metadata")]
    public async Task<ActionResult<MetadataSettingsResponse>> GetMetadata(CancellationToken cancellationToken)
        => Ok(await settingsService.GetMetadataAsync(cancellationToken));

    [HttpPut("metadata")]
    public async Task<ActionResult<MetadataSettingsResponse>> UpdateMetadata(
        [FromBody] UpdateMetadataSettingsRequest request,
        CancellationToken cancellationToken)
        => Ok(await settingsService.UpdateMetadataAsync(request, cancellationToken));

    [HttpGet("media")]
    public async Task<ActionResult<MediaManagementSettingsResponse>> GetMedia(CancellationToken cancellationToken)
        => Ok(await settingsService.GetMediaAsync(cancellationToken));

    [HttpGet("network")]
    public async Task<ActionResult<NetworkSettingsResponse>> GetNetwork(CancellationToken cancellationToken)
        => Ok(await settingsService.GetNetworkAsync(cancellationToken));

    [HttpPut("media")]
    public async Task<ActionResult<MediaManagementSettingsResponse>> UpdateMedia(
        [FromBody] UpdateMediaManagementSettingsRequest request,
        CancellationToken cancellationToken)
        => Ok(await settingsService.UpdateMediaAsync(request, cancellationToken));
}
