using Gamarr.Agent.Models;

namespace Gamarr.Agent.Services;

public interface IPackageJobExecutor
{
    Task ExecuteAsync(Guid machineId, NextJobResponse job, Func<JobEventRequest, Task> reportEvent, CancellationToken cancellationToken);
}
