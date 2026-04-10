using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class LibraryScan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LibraryRootId { get; set; }
    public LibraryRoot? LibraryRoot { get; set; }
    public LibraryScanState State { get; set; } = LibraryScanState.Queued;
    public int DirectoriesScanned { get; set; }
    public int FilesScanned { get; set; }
    public int CandidatesDetected { get; set; }
    public int CandidatesImported { get; set; }
    public int ErrorsCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public ICollection<LibraryCandidate> Candidates { get; set; } = new List<LibraryCandidate>();
}
