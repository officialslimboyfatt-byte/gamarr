using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class JobEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Job? Job { get; set; }
    public int SequenceNumber { get; set; }
    public JobState State { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
