using System.Text.Json;
using Gamarr.Agent.Models;

namespace Gamarr.Agent.Services;

public sealed class MachineIdentityStore
{
    private static readonly string StateDirectory = AgentPathResolver.GetWritableGamarrRoot();

    private static readonly string StatePath = Path.Combine(StateDirectory, "agent-state.json");

    public async Task<MachineIdentity> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StateDirectory);

        if (File.Exists(StatePath))
        {
            await using var readStream = File.OpenRead(StatePath);
            var existing = await JsonSerializer.DeserializeAsync<MachineIdentity>(readStream, cancellationToken: cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var created = new MachineIdentity
        {
            StableKey = $"gamarr-{Environment.MachineName.ToLowerInvariant()}-{Guid.NewGuid():N}",
            Name = Environment.MachineName
        };

        await using var writeStream = File.Create(StatePath);
        await JsonSerializer.SerializeAsync(writeStream, created, cancellationToken: cancellationToken);
        return created;
    }

    public async Task SaveMachineIdAsync(MachineIdentity identity, Guid machineId, CancellationToken cancellationToken)
    {
        identity.MachineId = machineId;
        Directory.CreateDirectory(StateDirectory);
        await using var writeStream = File.Create(StatePath);
        await JsonSerializer.SerializeAsync(writeStream, identity, cancellationToken: cancellationToken);
    }
}
