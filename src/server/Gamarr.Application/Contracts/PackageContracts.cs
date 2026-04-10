using Gamarr.Domain.Enums;

namespace Gamarr.Application.Contracts;

public sealed record CreatePackageRequest(
    string Slug,
    string Name,
    string Description,
    string Notes,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> Genres,
    string Studio,
    int? ReleaseYear,
    string? CoverImagePath,
    CreatePackageVersionRequest Version,
    string? MetadataProvider = null,
    string? MetadataSourceUrl = null,
    string MetadataSelectionKind = "Unknown");

public sealed record UpdatePackageMetadataRequest(
    string Slug,
    string Name,
    string Description,
    string Notes,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> Genres,
    string Studio,
    int? ReleaseYear,
    string? CoverImagePath,
    string? MetadataProvider,
    string? MetadataSourceUrl,
    string MetadataSelectionKind);

public sealed record UpdatePackageInstallPlanRequest(
    string InstallStrategy,
    string InstallerFamily,
    string? InstallerPath,
    string? SilentArguments,
    string InstallDiagnostics,
    string? LaunchExecutablePath,
    string? UninstallScriptPath,
    string? UninstallArguments,
    IReadOnlyCollection<CreateDetectionRuleRequest> DetectionRules);

public sealed record CreatePackageVersionRequest(
    string VersionLabel,
    string SupportedOs,
    ArchitectureKind Architecture,
    InstallScriptKind InstallScriptKind,
    string InstallScriptPath,
    string? UninstallScriptPath,
    string? UninstallArguments,
    int TimeoutSeconds,
    string Notes,
    string InstallStrategy,
    string InstallerFamily,
    string? InstallerPath,
    string? SilentArguments,
    string InstallDiagnostics,
    string? LaunchExecutablePath,
    IReadOnlyCollection<CreatePackageMediaRequest> Media,
    IReadOnlyCollection<CreateDetectionRuleRequest> DetectionRules,
    IReadOnlyCollection<CreatePrerequisiteRequest> Prerequisites);

public sealed record CreatePackageMediaRequest(
    MediaType MediaType,
    string Label,
    string Path,
    int? DiscNumber,
    string? EntrypointHint,
    PackageSourceKind SourceKind,
    ScratchPolicy ScratchPolicy);

public sealed record CreateDetectionRuleRequest(
    string RuleType,
    string Value);

public sealed record CreatePrerequisiteRequest(
    string Name,
    string Notes);

public sealed record PackageResponse(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    string Notes,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> Genres,
    string Studio,
    int? ReleaseYear,
    string? CoverImagePath,
    string? MetadataProvider,
    string? MetadataSourceUrl,
    string MetadataSelectionKind,
    bool IsArchived,
    string? ArchivedReason,
    DateTimeOffset? ArchivedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyCollection<PackageVersionResponse> Versions);

public sealed record PackageVersionResponse(
    Guid Id,
    string VersionLabel,
    string SupportedOs,
    ArchitectureKind Architecture,
    InstallScriptKind InstallScriptKind,
    string InstallScriptPath,
    string? UninstallScriptPath,
    string? UninstallArguments,
    string ManifestFormatVersion,
    string ManifestJson,
    int TimeoutSeconds,
    string Notes,
    string InstallStrategy,
    string InstallerFamily,
    string? InstallerPath,
    string? SilentArguments,
    string InstallDiagnostics,
    string? LaunchExecutablePath,
    string ProcessingState,
    string? NormalizedAssetRootPath,
    DateTimeOffset? NormalizedAtUtc,
    string NormalizationDiagnostics,
    bool IsActive,
    IReadOnlyCollection<PackageMediaResponse> Media,
    IReadOnlyCollection<DetectionRuleResponse> DetectionRules,
    IReadOnlyCollection<PrerequisiteResponse> Prerequisites);

public sealed record NormalizationJobResponse(
    Guid Id,
    Guid PackageId,
    Guid PackageVersionId,
    string PackageName,
    string PackageVersionLabel,
    string State,
    string SourcePath,
    string Summary,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PackageMediaResponse(
    Guid Id,
    MediaType MediaType,
    string Label,
    string Path,
    int? DiscNumber,
    string? EntrypointHint,
    PackageSourceKind SourceKind,
    ScratchPolicy ScratchPolicy);

public sealed record DetectionRuleResponse(
    Guid Id,
    string RuleType,
    string Value);

public sealed record PrerequisiteResponse(
    Guid Id,
    string Name,
    string Notes);

public sealed record PackageManifestResponse(
    string ManifestFormatVersion,
    string ManifestJson);
