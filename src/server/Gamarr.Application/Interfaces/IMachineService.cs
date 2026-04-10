using Gamarr.Application.Contracts;

namespace Gamarr.Application.Interfaces;

public interface IMachineService
{
    Task<MachineResponse> RegisterAsync(RegisterMachineRequest request, CancellationToken cancellationToken);
    Task<MachineResponse?> HeartbeatAsync(Guid machineId, HeartbeatRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MachineResponse>> ListAsync(CancellationToken cancellationToken);
    Task<MachineResponse?> GetAsync(Guid machineId, CancellationToken cancellationToken);
    Task RemoveAsync(Guid machineId, CancellationToken cancellationToken);
}
