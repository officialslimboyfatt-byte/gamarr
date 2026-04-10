using Gamarr.Domain.Enums;

namespace Gamarr.Application.Contracts;

public sealed record LibraryTitleResponse(
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
    string VersionLabel,
    InstallScriptKind InstallScriptKind,
    string? LaunchExecutablePath,
    LibraryInstallState InstallState,
    DateTimeOffset? LastValidatedAtUtc,
    bool IsInstallStateStale,
    string? ValidationSummary,
    bool CanValidate,
    bool CanUninstall,
    Guid? LastJobId,
    JobState? LatestJobState,
    JobActionType? LatestJobActionType,
    DateTimeOffset? LatestJobCreatedAtUtc,
    string SourceSummary,
    string SourceHealth,
    int SourceConflictCount,
    string InstallStrategy,
    string ProcessingState,
    string SupportedInstallPath,
    string InstallReadiness,
    string PlayReadiness,
    bool IsInstallable,
    bool CanInstall,
    bool CanPlay,
    string? ReviewRequiredReason,
    string RecipeDiagnostics,
    string NormalizationDiagnostics,
    string? NormalizedAssetRootPath,
    DateTimeOffset? NormalizedAtUtc,
    string? MetadataProvider,
    string? MetadataSourceUrl,
    string MetadataSelectionKind,
    string MetadataStatus,
    string? MetadataPrimarySource,
    double? MetadataConfidence,
    string? PosterImageUrl,
    string? BackdropImageUrl,
    string StoreDescription,
    bool IsArchived,
    string? ArchivedReason,
    DateTimeOffset? ArchivedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record LibraryTitleDetailResponse(
    LibraryTitleResponse Title,
    IReadOnlyCollection<LibraryCandidateSourceResponse> Sources,
    IReadOnlyCollection<DetectionRuleResponse> DetectionRules,
    string InstallScriptPath,
    string? UninstallScriptPath,
    string? UninstallArguments,
    string Notes,
    IReadOnlyCollection<LibrarySourceConflictResponse> SourceConflicts,
    JobResponse? LatestJob);

public sealed record PlayLibraryTitleRequest(Guid MachineId);
public sealed record LibraryMachineActionRequest(Guid MachineId);
public sealed record ArchiveLibraryTitleRequest(string? Reason);

public sealed record PlayLibraryTitleResponse(
    Guid PackageId,
    Guid MachineId,
    JobActionType ActionType,
    LibraryInstallState PreviousInstallState,
    JobResponse Job);

public sealed record ResetLibraryRequest(
    bool PreserveRoots = true,
    bool DeleteNormalizedAssets = true);

public sealed record ResetLibraryResponse(
    int PackagesDeleted,
    int CandidatesDeleted,
    int ScansDeleted,
    int RootsDeleted,
    int JobsDeleted,
    int NormalizationJobsDeleted,
    int MountsDeleted,
    bool NormalizedAssetsDeleted,
    string? NormalizedAssetRootPath);

public enum LibraryInstallState
{
    NotInstalled = 1,
    Installing = 2,
    Installed = 3,
    Failed = 4,
    Uninstalling = 5
}

public sealed record CreateLibraryRootRequest(
    string DisplayName,
    string Path);

public sealed record LibraryRootResponse(
    Guid Id,
    string DisplayName,
    string Path,
    LibraryRootPathKind PathKind,
    LibraryRootContentKind ContentKind,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastScanStartedAtUtc,
    DateTimeOffset? LastScanCompletedAtUtc,
    LibraryScanState? LastScanState,
    string? LastScanError,
    bool IsReachable,
    string HealthSummary);

public sealed record LibraryScanResponse(
    Guid Id,
    Guid LibraryRootId,
    string RootDisplayName,
    string RootPath,
    LibraryScanState State,
    int DirectoriesScanned,
    int FilesScanned,
    int CandidatesDetected,
    int CandidatesImported,
    int ErrorsCount,
    string Summary,
    string? ErrorMessage,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record LibraryCandidateSourceResponse(
    string Label,
    string Path,
    MediaType MediaType,
    PackageSourceKind SourceKind,
    ScratchPolicy ScratchPolicy,
    int? DiscNumber,
    string? EntrypointHint,
    bool HintFilePresent = false);

public sealed record LibraryCandidateResponse(
    Guid Id,
    Guid LibraryRootId,
    Guid LibraryScanId,
    Guid? PackageId,
    string RootDisplayName,
    DateTimeOffset? ScanStartedAtUtc,
    LibraryCandidateStatus Status,
    string Title,
    string Description,
    string Studio,
    int? ReleaseYear,
    string? CoverImagePath,
    IReadOnlyCollection<string> Genres,
    string? MetadataProvider,
    string? MetadataSourceUrl,
    double ConfidenceScore,
    string MetadataStatus,
    string? MetadataPrimarySource,
    string? PosterImageUrl,
    string? BackdropImageUrl,
    string StoreDescription,
    string PrimaryPath,
    int SourceCount,
    bool HintFilePresent,
    string InstallStrategy,
    bool IsInstallable,
    string RecipeDiagnostics,
    string MatchDecision,
    string MatchSummary,
    IReadOnlyCollection<string> WinningSignals,
    IReadOnlyCollection<string> WarningSignals,
    IReadOnlyCollection<ProviderDiagnosticResponse> ProviderDiagnostics,
    IReadOnlyCollection<MetadataMatchOptionResponse> AlternativeMatches,
    string? SelectedMatchKey,
    IReadOnlyCollection<LibrarySourceConflictResponse> SourceConflicts,
    IReadOnlyCollection<LibraryCandidateSourceResponse> Sources,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record MergeLibraryCandidateRequest(Guid PackageId);

public sealed record MetadataMatchOptionResponse(
    string Key,
    string Provider,
    string Title,
    string Description,
    int? ReleaseYear,
    string Studio,
    string? CoverImagePath,
    string? BackdropImageUrl,
    IReadOnlyCollection<string> ScreenshotImageUrls,
    string? SourceUrl,
    IReadOnlyCollection<string> Genres,
    IReadOnlyCollection<string> Themes,
    IReadOnlyCollection<string> Platforms,
    double Score,
    string ReasonSummary);

public sealed record ProviderDiagnosticResponse(
    string Provider,
    string SearchStatus,
    int CandidateCount,
    double? TopScore,
    bool IsWinner,
    string Summary,
    IReadOnlyCollection<string> TopTitles);

public sealed record SelectLibraryCandidateMatchRequest(
    string? MatchKey,
    bool LocalOnly);

public sealed record LibrarySourceConflictResponse(
    string ConflictType,
    string Path,
    Guid PackageId,
    string PackageName);

public sealed record LibraryMetadataSnapshotResponse(
    string Title,
    string Description,
    string Studio,
    int? ReleaseYear,
    string? CoverImagePath,
    IReadOnlyCollection<string> Genres,
    string? MetadataProvider,
    string? MetadataSourceUrl,
    string MetadataSelectionKind);

public sealed record LibraryReconcilePreviewResponse(
    Guid PackageId,
    string LocalTitle,
    string LocalDescription,
    LibraryMetadataSnapshotResponse Current,
    LibraryMetadataSnapshotResponse LocalOnly,
    string MatchSummary,
    IReadOnlyCollection<string> WinningSignals,
    IReadOnlyCollection<string> WarningSignals,
    IReadOnlyCollection<ProviderDiagnosticResponse> ProviderDiagnostics,
    IReadOnlyCollection<MetadataMatchOptionResponse> AlternativeMatches,
    IReadOnlyCollection<LibrarySourceConflictResponse> SourceConflicts,
    string InstallStrategy,
    string RecipeDiagnostics);

public sealed record ApplyLibraryReconcileRequest(
    string? MatchKey,
    bool LocalOnly);

public sealed record ManualMetadataSearchRequest(string Query);

public sealed record ManualMetadataSearchResponse(
    string Query,
    string LocalTitle,
    string MatchSummary,
    IReadOnlyCollection<string> WinningSignals,
    IReadOnlyCollection<string> WarningSignals,
    IReadOnlyCollection<ProviderDiagnosticResponse> ProviderDiagnostics,
    IReadOnlyCollection<MetadataMatchOptionResponse> AlternativeMatches);

public sealed record ApplyManualMetadataMatchRequest(
    string Query,
    string MatchKey);

public sealed record ReplaceMergeTargetRequest(Guid PackageId);

public sealed record BulkReMatchResponse(
    int ProcessedCount,
    int AutoImportedCount,
    int NowReviewableCount,
    int StillUnmatchedCount);
