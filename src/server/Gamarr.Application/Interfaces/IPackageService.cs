using Gamarr.Application.Contracts;

namespace Gamarr.Application.Interfaces;

public interface IPackageService
{
    Task<PackageResponse> CreateAsync(CreatePackageRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PackageResponse>> ListAsync(CancellationToken cancellationToken);
    Task<PackageResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PackageResponse> UpdateMetadataAsync(Guid id, UpdatePackageMetadataRequest request, CancellationToken cancellationToken);
    Task<PackageResponse> UpdateInstallPlanAsync(Guid id, UpdatePackageInstallPlanRequest request, CancellationToken cancellationToken);
    Task<PackageResponse> ArchiveAsync(Guid id, string? reason, CancellationToken cancellationToken);
    Task<PackageResponse> RestoreAsync(Guid id, CancellationToken cancellationToken);
    Task<PackageResponse> ReNormalizeAsync(Guid id, CancellationToken cancellationToken);
    Task<int> BulkReNormalizeNeedsReviewAsync(CancellationToken cancellationToken);
}
