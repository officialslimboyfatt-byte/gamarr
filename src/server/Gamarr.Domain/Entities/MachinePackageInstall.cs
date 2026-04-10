namespace Gamarr.Domain.Entities;

public sealed class MachinePackageInstall
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public Guid PackageId { get; set; }
    public Package? Package { get; set; }
    public Guid PackageVersionId { get; set; }
    public PackageVersion? PackageVersion { get; set; }
    public string State { get; set; } = "Unknown";
    public DateTimeOffset? InstalledAtUtc { get; set; }
    public DateTimeOffset? LastValidatedAtUtc { get; set; }
    public string? ValidationSummary { get; set; }
    public string? LastKnownLaunchPath { get; set; }
    public string? LastKnownInstallLocation { get; set; }
    public string? ResolvedUninstallCommand { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
