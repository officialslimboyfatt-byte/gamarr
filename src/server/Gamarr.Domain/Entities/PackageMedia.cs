using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class PackageMedia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageVersionId { get; set; }
    public PackageVersion? PackageVersion { get; set; }
    public MediaType MediaType { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int? DiscNumber { get; set; }
    public string? EntrypointHint { get; set; }
    public PackageSourceKind SourceKind { get; set; } = PackageSourceKind.Auto;
    public ScratchPolicy ScratchPolicy { get; set; } = ScratchPolicy.Temporary;
}
