namespace Gamarr.Application.Interfaces;

public interface IJobDispatchPublisher
{
    Task PublishJobCreatedAsync(Guid jobId, Guid machineId, CancellationToken cancellationToken);
    Task PublishJobUpdatedAsync(Guid jobId, CancellationToken cancellationToken);
}
