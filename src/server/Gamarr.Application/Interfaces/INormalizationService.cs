using Gamarr.Application.Contracts;

namespace Gamarr.Application.Interfaces;

public interface INormalizationService
{
    Task QueuePackageAsync(Guid packageId, CancellationToken cancellationToken);
    Task QueuePackageVersionAsync(Guid packageId, Guid packageVersionId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<NormalizationJobResponse>> ListJobsAsync(Guid? packageId, string? state, CancellationToken cancellationToken);
}
