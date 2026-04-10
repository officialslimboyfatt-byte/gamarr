using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class LibraryRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public LibraryRootPathKind PathKind { get; set; } = LibraryRootPathKind.Local;
    public LibraryRootContentKind ContentKind { get; set; } = LibraryRootContentKind.Unknown;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastScanStartedAtUtc { get; set; }
    public DateTimeOffset? LastScanCompletedAtUtc { get; set; }
    public LibraryScanState? LastScanState { get; set; }
    public string? LastScanError { get; set; }
    public ICollection<LibraryScan> Scans { get; set; } = new List<LibraryScan>();
    public ICollection<LibraryCandidate> Candidates { get; set; } = new List<LibraryCandidate>();
}
