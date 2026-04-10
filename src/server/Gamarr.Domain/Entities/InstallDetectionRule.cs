namespace Gamarr.Domain.Entities;

public sealed class InstallDetectionRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageVersionId { get; set; }
    public PackageVersion? PackageVersion { get; set; }
    public string RuleType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
