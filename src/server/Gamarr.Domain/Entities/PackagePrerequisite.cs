namespace Gamarr.Domain.Entities;

public sealed class PackagePrerequisite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageVersionId { get; set; }
    public PackageVersion? PackageVersion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
