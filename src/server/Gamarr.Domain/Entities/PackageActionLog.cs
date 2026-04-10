using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class PackageActionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Job? Job { get; set; }
    public LogLevelKind Level { get; set; } = LogLevelKind.Information;
    public string Source { get; set; } = "agent";
    public string Message { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
