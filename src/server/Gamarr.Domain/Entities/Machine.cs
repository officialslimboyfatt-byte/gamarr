using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class Machine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StableKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public ArchitectureKind Architecture { get; set; }
    public string AgentVersion { get; set; } = string.Empty;
    public MachineStatus Status { get; set; } = MachineStatus.Unknown;
    public DateTimeOffset RegisteredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastHeartbeatUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<MachineCapability> Capabilities { get; set; } = new List<MachineCapability>();
}
