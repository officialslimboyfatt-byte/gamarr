using Gamarr.Application.Contracts;
using Gamarr.Domain.Enums;

namespace Gamarr.Application.Interfaces;

public interface IJobService
{
    Task<JobResponse> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<JobResponse>> ListAsync(Guid? machineId, JobState? state, JobActionType? actionType, string? search, string? scope, CancellationToken cancellationToken);
    Task<JobResponse?> GetAsync(Guid jobId, CancellationToken cancellationToken);
    Task<JobResponse?> FindActiveAsync(Guid packageId, Guid machineId, CancellationToken cancellationToken);
    Task<JobResponse?> ClaimAsync(Guid jobId, ClaimJobRequest request, CancellationToken cancellationToken);
    Task<JobResponse?> AddEventAsync(Guid jobId, JobEventRequest request, CancellationToken cancellationToken);
    Task<NextJobResponse?> GetNextJobAsync(Guid machineId, CancellationToken cancellationToken);
    Task<JobResponse> CancelJobAsync(Guid jobId, CancellationToken cancellationToken);
}
