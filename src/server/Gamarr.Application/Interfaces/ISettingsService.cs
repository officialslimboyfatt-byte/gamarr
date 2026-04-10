using Gamarr.Application.Contracts;

namespace Gamarr.Application.Interfaces;

public interface ISettingsService
{
    Task EnsureDefaultsAsync(CancellationToken cancellationToken);
    Task<SettingsResponse> GetAsync(CancellationToken cancellationToken);
    Task<MetadataSettingsResponse> GetMetadataAsync(CancellationToken cancellationToken);
    Task<MediaManagementSettingsResponse> GetMediaAsync(CancellationToken cancellationToken);
    Task<NetworkSettingsResponse> GetNetworkAsync(CancellationToken cancellationToken);
    Task<MetadataSettingsRuntime> GetMetadataRuntimeAsync(CancellationToken cancellationToken);
    Task<MediaManagementSettingsRuntime> GetMediaRuntimeAsync(CancellationToken cancellationToken);
    Task<MetadataSettingsResponse> UpdateMetadataAsync(UpdateMetadataSettingsRequest request, CancellationToken cancellationToken);
    Task<MediaManagementSettingsResponse> UpdateMediaAsync(UpdateMediaManagementSettingsRequest request, CancellationToken cancellationToken);
}
