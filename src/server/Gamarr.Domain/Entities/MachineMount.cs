namespace Gamarr.Domain.Entities;

public sealed class MachineMount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public string IsoPath { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? DriveLetter { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
