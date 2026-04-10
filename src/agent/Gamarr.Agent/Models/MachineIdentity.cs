namespace Gamarr.Agent.Models;

public sealed class MachineIdentity
{
    public Guid MachineId { get; set; }
    public string StableKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
