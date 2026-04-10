namespace Gamarr.Domain.Entities;

public sealed class NormalizationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public Package? Package { get; set; }
    public Guid PackageVersionId { get; set; }
    public PackageVersion? PackageVersion { get; set; }
    public string State { get; set; } = "Queued";
    public string SourcePath { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
