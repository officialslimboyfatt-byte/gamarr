namespace Gamarr.Domain.Entities;

public sealed class MachineCapability
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public string Capability { get; set; } = string.Empty;
}
