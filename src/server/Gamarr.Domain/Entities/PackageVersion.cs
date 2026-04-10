using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class PackageVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public Package? Package { get; set; }
    public string VersionLabel { get; set; } = string.Empty;
    public string SupportedOs { get; set; } = string.Empty;
    public ArchitectureKind Architecture { get; set; }
    public InstallScriptKind InstallScriptKind { get; set; } = InstallScriptKind.MockRecipe;
    public string InstallScriptPath { get; set; } = string.Empty;
    public string? UninstallScriptPath { get; set; }
    public string? UninstallArguments { get; set; }
    public string ManifestFormatVersion { get; set; } = "gamarr.package/v1";
    public string ManifestJson { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 3600;
    public string Notes { get; set; } = string.Empty;
    public string InstallStrategy { get; set; } = "NeedsReview";
    public string InstallerFamily { get; set; } = "Unknown";
    public string? InstallerPath { get; set; }
    public string? SilentArguments { get; set; }
    public string InstallDiagnostics { get; set; } = string.Empty;
    public string? LaunchExecutablePath { get; set; }
    public string ProcessingState { get; set; } = "Discovered";
    public string? NormalizedAssetRootPath { get; set; }
    public DateTimeOffset? NormalizedAtUtc { get; set; }
    public string NormalizationDiagnostics { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<PackageMedia> Media { get; set; } = new List<PackageMedia>();
    public ICollection<InstallDetectionRule> DetectionRules { get; set; } = new List<InstallDetectionRule>();
    public ICollection<PackagePrerequisite> Prerequisites { get; set; } = new List<PackagePrerequisite>();
}
