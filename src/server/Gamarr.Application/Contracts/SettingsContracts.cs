namespace Gamarr.Application.Contracts;

public sealed record SettingsResponse(
    MetadataSettingsResponse Metadata,
    MediaManagementSettingsResponse Media,
    NetworkSettingsResponse Network);

public sealed record MetadataSettingsResponse(
    bool PreferIgdb,
    bool IgdbEnabled,
    string? IgdbClientId,
    bool HasIgdbClientSecret,
    bool IgdbConfigured,
    bool UseSteamFallback,
    double AutoImportThreshold,
    double ReviewThreshold,
    string ProviderStatus);

public sealed record UpdateMetadataSettingsRequest(
    bool PreferIgdb,
    bool IgdbEnabled,
    string? IgdbClientId,
    string? IgdbClientSecret,
    bool ClearIgdbClientSecret,
    bool UseSteamFallback,
    double AutoImportThreshold,
    double ReviewThreshold);

public sealed record MediaManagementSettingsResponse(
    string? DefaultLibraryRootPath,
    string? NormalizedAssetRootPath,
    bool AutoScanOnRootCreate,
    bool AutoNormalizeOnImport,
    bool AutoImportHighConfidenceMatches,
    IReadOnlyCollection<string> IncludePatterns,
    IReadOnlyCollection<string> ExcludePatterns,
    string SupportedInstallPathSummary);

public sealed record UpdateMediaManagementSettingsRequest(
    string? DefaultLibraryRootPath,
    string? NormalizedAssetRootPath,
    bool AutoScanOnRootCreate,
    bool AutoNormalizeOnImport,
    bool AutoImportHighConfidenceMatches,
    IReadOnlyCollection<string> IncludePatterns,
    IReadOnlyCollection<string> ExcludePatterns);

public sealed record MetadataSettingsRuntime(
    bool PreferIgdb,
    bool IgdbEnabled,
    string? IgdbClientId,
    string? IgdbClientSecret,
    bool UseSteamFallback,
    double AutoImportThreshold,
    double ReviewThreshold);

public sealed record MediaManagementSettingsRuntime(
    string? DefaultLibraryRootPath,
    string? NormalizedAssetRootPath,
    bool AutoScanOnRootCreate,
    bool AutoNormalizeOnImport,
    bool AutoImportHighConfidenceMatches,
    IReadOnlyCollection<string> IncludePatterns,
    IReadOnlyCollection<string> ExcludePatterns);

public sealed record NetworkSettingsResponse(
    string PublicServerUrl,
    string AgentServerUrl,
    string ApiListenUrls,
    string WebListenHost,
    bool LanEnabled,
    string Summary);
