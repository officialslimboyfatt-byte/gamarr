using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class LibraryCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LibraryRootId { get; set; }
    public LibraryRoot? LibraryRoot { get; set; }
    public Guid LibraryScanId { get; set; }
    public LibraryScan? LibraryScan { get; set; }
    public Guid? PackageId { get; set; }
    public Package? Package { get; set; }
    public LibraryCandidateStatus Status { get; set; } = LibraryCandidateStatus.PendingReview;
    public string LocalTitle { get; set; } = string.Empty;
    public string LocalNormalizedTitle { get; set; } = string.Empty;
    public string LocalDescription { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Studio { get; set; } = string.Empty;
    public int? ReleaseYear { get; set; }
    public string? CoverImagePath { get; set; }
    public string GenresSerialized { get; set; } = string.Empty;
    public string? MetadataProvider { get; set; }
    public string? MetadataSourceUrl { get; set; }
    public double ConfidenceScore { get; set; }
    public string MatchDecision { get; set; } = "Review";
    public string MatchSummary { get; set; } = string.Empty;
    public string WinningSignalsJson { get; set; } = "[]";
    public string WarningSignalsJson { get; set; } = "[]";
    public string ProviderDiagnosticsJson { get; set; } = "[]";
    public string AlternativeMatchesJson { get; set; } = "[]";
    public string? SelectedMatchKey { get; set; }
    public string PrimaryPath { get; set; } = string.Empty;
    public string SourcesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public IReadOnlyCollection<string> Genres =>
        GenresSerialized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
