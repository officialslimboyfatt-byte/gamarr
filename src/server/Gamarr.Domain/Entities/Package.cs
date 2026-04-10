namespace Gamarr.Domain.Entities;

public sealed class Package
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string TagsSerialized { get; set; } = string.Empty;
    public string GenresSerialized { get; set; } = string.Empty;
    public string Studio { get; set; } = string.Empty;
    public int? ReleaseYear { get; set; }
    public string? CoverImagePath { get; set; }
    public string? MetadataProvider { get; set; }
    public string? MetadataSourceUrl { get; set; }
    public string MetadataSelectionKind { get; set; } = "Unknown";
    public bool IsArchived { get; set; }
    public string? ArchivedReason { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<PackageVersion> Versions { get; set; } = new List<PackageVersion>();

    public IReadOnlyCollection<string> Tags =>
        TagsSerialized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyCollection<string> Genres =>
        GenresSerialized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
