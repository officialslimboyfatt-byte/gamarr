using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gamarr.Application.Contracts;
using Gamarr.Application.Exceptions;
using Gamarr.Application.Interfaces;
using Gamarr.Application.Services;
using Gamarr.Domain.Entities;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Models;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gamarr.Infrastructure.Services;

public sealed partial class LibraryService(
    GamarrDbContext dbContext,
    IJobService jobService,
    IPackageService packageService,
    ILibraryMetadataProvider metadataProvider,
    ISettingsService settingsService,
    IServiceScopeFactory scopeFactory) : ILibraryService
{
    private const int MaxScanDepth = 5;

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$recycle.bin", "system volume information", ".git", "temp", "tmp", "cache", "logs", "redistributables", "redist"
    };

    private static readonly string[] JunkDirectoryTokens =
    [
        "bin", "bin32", "bin64", "tools", "tool", "crack", "no cd", "nocd", "keygen", "trainer", "patch", "fix", "punkbuster", "pb", "directx", "support"
    ];

    private static readonly string[] NoiseTitleTokens =
    [
        "game", "pc", "dvd", "cd", "disc", "install", "setup", "rip", "reloaded", "skidrow", "razor1911", "flt", "gog", "steamrip", "portable",
        // Region and format codes from ROM/archive naming conventions
        "usa", "eur", "europe", "jpn", "japan", "pal", "ntsc", "ntscu", "ntscc", "ntscj", "rev", "multi", "multilanguage",
        // Common articles and prepositions that add noise to token overlap scoring
        "the", "a", "an", "and", "of", "or", "in", "on", "at", "to", "for"
    ];

    private static readonly Dictionary<string, string> RomanNumerals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i"] = "1", ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5",
        ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
        ["xi"] = "11", ["xii"] = "12"
    };

    private static readonly string[] EditionSensitiveTokens =
    [
        "remastered", "redux", "definitive", "legendary", "complete", "ultimate", "enhanced", "anniversary", "demo", "beta", "alpha", "trial",
        "edition", "collection", "bundle", "pack", "goty", "deluxe", "premium", "gold", "platinum"
    ];

    private static readonly string[] SubtitleSeparators =
    [
        ":", " - ", " – ", " — "
    ];

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".cmd", ".bat"
    };

    private static readonly HashSet<string> DiskImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".img", ".vhd", ".vhdx", ".bin", ".cue", ".mdf", ".mds", ".nrg", ".ccd", ".cdi"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<LibraryTitleResponse>> ListAsync(
        Guid? machineId,
        string? genre,
        string? studio,
        int? year,
        string? sortBy,
        CancellationToken cancellationToken)
    {
        var packages = await QueryPackages(includeArchived: false).ToListAsync(cancellationToken);
        IEnumerable<Package> filtered = packages;

        if (!string.IsNullOrWhiteSpace(genre))
        {
            filtered = filtered.Where(x => x.Genres.Any(g => string.Equals(g, genre, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(studio))
        {
            filtered = filtered.Where(x => string.Equals(x.Studio, studio, StringComparison.OrdinalIgnoreCase));
        }

        if (year.HasValue)
        {
            filtered = filtered.Where(x => x.ReleaseYear == year.Value);
        }

        filtered = (sortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "year" => filtered.OrderByDescending(x => x.ReleaseYear ?? 0).ThenBy(x => x.Name),
            "studio" => filtered.OrderBy(x => x.Studio).ThenBy(x => x.Name),
            "recent" => filtered.OrderByDescending(x => x.UpdatedAtUtc),
            _ => filtered.OrderBy(x => x.Name)
        };

        return filtered.Select(x => ToLibraryResponse(x, machineId)).ToArray();
    }

    public async Task<LibraryTitleDetailResponse?> GetAsync(Guid packageId, Guid? machineId, CancellationToken cancellationToken)
    {
        var package = await QueryPackages(includeArchived: true).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken);
        if (package is null)
        {
            return null;
        }

        var version = package.Versions.OrderByDescending(v => v.IsActive).First();
        var latestJob = machineId.HasValue
            ? await dbContext.Jobs
                .Include(j => j.Package)
                .Include(j => j.PackageVersion)
                .Include(j => j.Machine)
                .Include(j => j.Events)
                .Include(j => j.Logs)
                .Where(j => j.PackageId == package.Id && j.MachineId == machineId.Value)
                .OrderByDescending(j => j.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var sourceConflicts = await FindSourceConflictsAsync(package.Id, version.Media.Select(m => m.Path), cancellationToken);

        return new LibraryTitleDetailResponse(
            ToLibraryResponse(package, machineId),
            version.Media.Select(ToCandidateSourceResponse).ToArray(),
            version.DetectionRules.Select(d => new DetectionRuleResponse(d.Id, d.RuleType, d.Value)).ToArray(),
            version.InstallScriptPath,
            version.UninstallScriptPath,
            version.UninstallArguments,
            package.Notes,
            sourceConflicts,
            latestJob?.ToResponse());
    }

    public async Task<LibraryReconcilePreviewResponse> PreviewReconcileAsync(Guid packageId, CancellationToken cancellationToken)
    {
        var package = await QueryPackages(includeArchived: true).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");

        var version = package.Versions.OrderByDescending(v => v.IsActive).First();
        var localGroup = BuildMutableCandidateGroup(version);
        var entrypointHint = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).FirstOrDefault()?.EntrypointHint;
        var metadataSearch = await SearchWithFallbackAsync(localGroup.Title, entrypointHint, cancellationToken);
        var metadataSettings = await settingsService.GetMetadataRuntimeAsync(cancellationToken);
        var evaluation = EvaluateMetadata(localGroup, metadataSearch, metadataSettings);
        var sourceConflicts = await FindSourceConflictsAsync(package.Id, version.Media.Select(m => m.Path), cancellationToken);
        var installPlan = BuildInstallPlan(package, version.Media.Select(ToCandidateSourceResponse).ToArray());

        return new LibraryReconcilePreviewResponse(
            package.Id,
            localGroup.Title,
            localGroup.Description,
            new LibraryMetadataSnapshotResponse(
                package.Name,
                package.Description,
                package.Studio,
                package.ReleaseYear,
                package.CoverImagePath,
                package.Genres.ToArray(),
                package.MetadataProvider,
                package.MetadataSourceUrl,
                package.MetadataSelectionKind),
            new LibraryMetadataSnapshotResponse(
                localGroup.Title,
                localGroup.Description,
                string.Empty,
                null,
                null,
                Array.Empty<string>(),
                null,
                null,
                "LocalOnly"),
            evaluation.Summary,
            evaluation.WinningSignals,
            evaluation.WarningSignals,
            evaluation.ProviderDiagnostics,
            evaluation.AlternativeMatches,
            sourceConflicts,
            installPlan.Strategy,
            installPlan.Diagnostics);
    }

    public async Task<LibraryTitleDetailResponse> ApplyReconcileAsync(Guid packageId, ApplyLibraryReconcileRequest request, Guid? machineId, CancellationToken cancellationToken)
    {
        var package = await QueryPackages(includeArchived: true).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");

        var version = package.Versions.OrderByDescending(v => v.IsActive).First();
        var localGroup = BuildMutableCandidateGroup(version);
        var entrypointHint = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).FirstOrDefault()?.EntrypointHint;
        var metadataSearch = await SearchWithFallbackAsync(localGroup.Title, entrypointHint, cancellationToken);
        var metadataSettings = await settingsService.GetMetadataRuntimeAsync(cancellationToken);
        var evaluation = EvaluateMetadata(localGroup, metadataSearch, metadataSettings);

        UpdatePackageMetadataRequest updateRequest;
        if (request.LocalOnly)
        {
            var slug = await EnsureUniqueSlugForUpdateAsync(package.Id, NormalizeTitle(localGroup.Title), cancellationToken);
            updateRequest = BuildLocalOnlyMetadataUpdate(package, localGroup, slug);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.MatchKey))
            {
                throw new AppValidationException("MatchKey is required when LocalOnly is false.");
            }

            var selected = evaluation.AlternativeMatches.FirstOrDefault(x => string.Equals(x.Key, request.MatchKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new AppNotFoundException("Metadata match option not found.");

            var slug = await EnsureUniqueSlugForUpdateAsync(package.Id, NormalizeTitle(selected.Title), cancellationToken);
            updateRequest = BuildSelectedMetadataUpdate(package, localGroup, selected, slug);
        }

        await packageService.UpdateMetadataAsync(packageId, updateRequest, cancellationToken);
        return await GetAsync(packageId, machineId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");
    }

    public async Task<ManualMetadataSearchResponse> SearchPackageMetadataAsync(Guid packageId, ManualMetadataSearchRequest request, CancellationToken cancellationToken)
    {
        var package = await QueryPackages(includeArchived: true).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");

        var version = package.Versions.OrderByDescending(v => v.IsActive).First();
        var localGroup = BuildMutableCandidateGroup(version);
        var entrypointHint = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).FirstOrDefault()?.EntrypointHint;
        return await SearchMetadataAsync(localGroup, request.Query, entrypointHint, cancellationToken);
    }

    public async Task<LibraryTitleDetailResponse> ApplyPackageMetadataSearchAsync(Guid packageId, ApplyManualMetadataMatchRequest request, Guid? machineId, CancellationToken cancellationToken)
    {
        var package = await QueryPackages(includeArchived: true).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");

        var version = package.Versions.OrderByDescending(v => v.IsActive).First();
        var localGroup = BuildMutableCandidateGroup(version);
        var entrypointHint = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).FirstOrDefault()?.EntrypointHint;
        var search = await SearchMetadataAsync(localGroup, request.Query, entrypointHint, cancellationToken);
        var selected = search.AlternativeMatches.FirstOrDefault(x => string.Equals(x.Key, request.MatchKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new AppNotFoundException("Metadata match option not found.");

        var slug = await EnsureUniqueSlugForUpdateAsync(package.Id, NormalizeTitle(selected.Title), cancellationToken);
        var updateRequest = BuildSelectedMetadataUpdate(package, localGroup, selected, slug);
        await packageService.UpdateMetadataAsync(packageId, updateRequest, cancellationToken);

        return await GetAsync(packageId, machineId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");
    }

    public async Task<LibraryTitleDetailResponse> ArchiveAsync(Guid packageId, string? reason, Guid? machineId, CancellationToken cancellationToken)
    {
        await packageService.ArchiveAsync(packageId, reason, cancellationToken);
        return await GetAsync(packageId, machineId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");
    }

    public async Task<LibraryTitleDetailResponse> RestoreAsync(Guid packageId, Guid? machineId, CancellationToken cancellationToken)
    {
        await packageService.RestoreAsync(packageId, cancellationToken);
        return await GetAsync(packageId, machineId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");
    }

    public async Task<PlayLibraryTitleResponse> PlayAsync(Guid packageId, PlayLibraryTitleRequest request, CancellationToken cancellationToken)
    {
        if (request.MachineId == Guid.Empty)
        {
            throw new AppValidationException("MachineId is required.");
        }

        var package = await QueryPackages(includeArchived: false).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");

        var version = package.Versions.FirstOrDefault(x => x.IsActive)
            ?? throw new AppConflictException("Library title has no active package version.");

        var sourceHealth = ResolveSourceHealth(version);
        var reviewRequiredReason = ResolveReviewRequiredReason(version, sourceHealth);
        var supportedInstallPath = ResolveSupportedInstallPath(version, version.InstallStrategy);

        var active = await jobService.FindActiveAsync(packageId, request.MachineId, cancellationToken);
        if (active is not null)
        {
            return new PlayLibraryTitleResponse(packageId, request.MachineId, active.ActionType, ResolveInstallState(packageId, request.MachineId), active);
        }

        var priorState = ResolveInstallState(packageId, request.MachineId);
        var machineInstall = FindMachineInstall(version.Id, request.MachineId);
        var canPlay = priorState == LibraryInstallState.Installed && HasLaunchCapability(version);
        var installReadiness = ResolveInstallReadiness(supportedInstallPath, sourceHealth, reviewRequiredReason);
        var canInstall = !canPlay &&
                         priorState != LibraryInstallState.Installing &&
                         string.Equals(installReadiness, "Ready", StringComparison.OrdinalIgnoreCase);

        if (!canPlay && !canInstall)
        {
            throw new AppConflictException(reviewRequiredReason ?? $"This title is not on a supported one-click install path. Current path: {supportedInstallPath}.");
        }

        var actionType = canPlay
            ? ShouldRefreshInstalledState(machineInstall) ? JobActionType.Validate : JobActionType.Launch
            : JobActionType.Install;
        var job = await jobService.CreateAsync(
            new CreateJobRequest(package.Id, request.MachineId, actionType, "library-play"),
            cancellationToken);

        return new PlayLibraryTitleResponse(packageId, request.MachineId, actionType, priorState, job);
    }

    public async Task<PlayLibraryTitleResponse> ValidateInstallAsync(Guid packageId, LibraryMachineActionRequest request, CancellationToken cancellationToken)
    {
        if (request.MachineId == Guid.Empty)
        {
            throw new AppValidationException("MachineId is required.");
        }

        var package = await QueryPackages(includeArchived: false).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");

        var active = await jobService.FindActiveAsync(packageId, request.MachineId, cancellationToken);
        if (active is not null)
        {
            return new PlayLibraryTitleResponse(packageId, request.MachineId, active.ActionType, ResolveInstallState(packageId, request.MachineId), active);
        }

        var priorState = ResolveInstallState(packageId, request.MachineId);
        var job = await jobService.CreateAsync(
            new CreateJobRequest(package.Id, request.MachineId, JobActionType.Validate, "library-validate"),
            cancellationToken);

        return new PlayLibraryTitleResponse(packageId, request.MachineId, JobActionType.Validate, priorState, job);
    }

    public async Task<PlayLibraryTitleResponse> UninstallAsync(Guid packageId, LibraryMachineActionRequest request, CancellationToken cancellationToken)
    {
        if (request.MachineId == Guid.Empty)
        {
            throw new AppValidationException("MachineId is required.");
        }

        var package = await QueryPackages(includeArchived: false).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");

        var version = package.Versions.FirstOrDefault(x => x.IsActive)
            ?? throw new AppConflictException("Library title has no active package version.");

        var active = await jobService.FindActiveAsync(packageId, request.MachineId, cancellationToken);
        if (active is not null)
        {
            return new PlayLibraryTitleResponse(packageId, request.MachineId, active.ActionType, ResolveInstallState(packageId, request.MachineId), active);
        }

        var priorState = ResolveInstallState(packageId, request.MachineId);
        if (priorState != LibraryInstallState.Installed)
        {
            throw new AppConflictException("Only installed titles can be uninstalled.");
        }

        var machineInstall = FindMachineInstall(version.Id, request.MachineId);
        if (!HasUninstallCapability(version, machineInstall))
        {
            throw new AppConflictException("No verified uninstall command is available for this title. Use Mark Not Installed after removing it manually.");
        }

        var job = await jobService.CreateAsync(
            new CreateJobRequest(package.Id, request.MachineId, JobActionType.Uninstall, "library-uninstall"),
            cancellationToken);

        return new PlayLibraryTitleResponse(packageId, request.MachineId, JobActionType.Uninstall, priorState, job);
    }

    public async Task<LibraryTitleDetailResponse> MarkNotInstalledAsync(Guid packageId, LibraryMachineActionRequest request, CancellationToken cancellationToken)
    {
        if (request.MachineId == Guid.Empty)
        {
            throw new AppValidationException("MachineId is required.");
        }

        var package = await QueryPackages(includeArchived: true).FirstOrDefaultAsync(x => x.Id == packageId, cancellationToken)
            ?? throw new AppNotFoundException("Library title not found.");
        var version = package.Versions.FirstOrDefault(x => x.IsActive)
            ?? throw new AppConflictException("Library title has no active package version.");

        var install = await dbContext.MachinePackageInstalls
            .FirstOrDefaultAsync(x => x.MachineId == request.MachineId && x.PackageVersionId == version.Id, cancellationToken);

        if (install is null)
        {
            install = new MachinePackageInstall
            {
                MachineId = request.MachineId,
                PackageId = package.Id,
                PackageVersionId = version.Id
            };
            dbContext.MachinePackageInstalls.Add(install);
        }

        install.State = "NotInstalled";
        install.InstalledAtUtc = null;
        install.LastValidatedAtUtc = DateTimeOffset.UtcNow;
        install.ValidationSummary = "Marked not installed by user.";
        install.LastKnownLaunchPath = null;
        install.LastKnownInstallLocation = null;
        install.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(packageId, request.MachineId, cancellationToken))!;
    }

    public async Task<ResetLibraryResponse> ResetAsync(ResetLibraryRequest request, CancellationToken cancellationToken)
    {
        var mediaSettings = await settingsService.GetMediaRuntimeAsync(cancellationToken);
        var normalizedAssetRootPath = string.IsNullOrWhiteSpace(mediaSettings.NormalizedAssetRootPath)
            ? null
            : mediaSettings.NormalizedAssetRootPath.Trim();

        var packagesDeleted = await dbContext.Packages.CountAsync(cancellationToken);
        var candidatesDeleted = await dbContext.LibraryCandidates.CountAsync(cancellationToken);
        var scansDeleted = await dbContext.LibraryScans.CountAsync(cancellationToken);
        var rootsDeleted = request.PreserveRoots ? 0 : await dbContext.LibraryRoots.CountAsync(cancellationToken);
        var jobsDeleted = await dbContext.Jobs.CountAsync(cancellationToken);
        var normalizationJobsDeleted = await dbContext.NormalizationJobs.CountAsync(cancellationToken);
        var mountsDeleted = await CountMachineMountsSafeAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.JobEvents.ExecuteDeleteAsync(cancellationToken);
        await dbContext.PackageActionLogs.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Jobs.ExecuteDeleteAsync(cancellationToken);
        await dbContext.MachinePackageInstalls.ExecuteDeleteAsync(cancellationToken);
        await DeleteMachineMountsSafeAsync(cancellationToken);

        await dbContext.LibraryCandidates.ExecuteDeleteAsync(cancellationToken);
        await dbContext.LibraryScans.ExecuteDeleteAsync(cancellationToken);

        await dbContext.NormalizationJobs.ExecuteDeleteAsync(cancellationToken);
        await dbContext.InstallDetectionRules.ExecuteDeleteAsync(cancellationToken);
        await dbContext.PackagePrerequisites.ExecuteDeleteAsync(cancellationToken);
        await dbContext.PackageMedia.ExecuteDeleteAsync(cancellationToken);
        await dbContext.PackageVersions.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Packages.ExecuteDeleteAsync(cancellationToken);

        if (request.PreserveRoots)
        {
            await dbContext.LibraryRoots.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ContentKind, LibraryRootContentKind.Unknown)
                .SetProperty(x => x.LastScanStartedAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.LastScanCompletedAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.LastScanState, (LibraryScanState?)null)
                .SetProperty(x => x.LastScanError, (string?)null)
                .SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        }
        else
        {
            await dbContext.LibraryRoots.ExecuteDeleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var normalizedAssetsDeleted = false;
        if (request.DeleteNormalizedAssets &&
            !string.IsNullOrWhiteSpace(normalizedAssetRootPath) &&
            Directory.Exists(normalizedAssetRootPath))
        {
            ClearDirectoryContents(normalizedAssetRootPath);
            normalizedAssetsDeleted = true;
        }

        return new ResetLibraryResponse(
            packagesDeleted,
            candidatesDeleted,
            scansDeleted,
            rootsDeleted,
            jobsDeleted,
            normalizationJobsDeleted,
            mountsDeleted,
            normalizedAssetsDeleted,
            normalizedAssetRootPath);
    }

    public async Task<IReadOnlyCollection<LibraryRootResponse>> ListRootsAsync(CancellationToken cancellationToken)
    {
        var roots = await dbContext.LibraryRoots.OrderBy(x => x.DisplayName).ToListAsync(cancellationToken);
        return roots.Select(ToRootResponse).ToArray();
    }

    public async Task<LibraryRootResponse> CreateRootAsync(CreateLibraryRootRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidateLibraryRootRequest(request);

        var normalizedPath = request.Path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathKind = normalizedPath.StartsWith(@"\\", StringComparison.Ordinal) ? LibraryRootPathKind.Unc : LibraryRootPathKind.Local;

        var exists = await dbContext.LibraryRoots.AnyAsync(x => x.Path == normalizedPath, cancellationToken);
        if (exists)
        {
            throw new AppConflictException($"A library root with path '{normalizedPath}' already exists.");
        }

        var root = new LibraryRoot
        {
            DisplayName = request.DisplayName.Trim(),
            Path = normalizedPath,
            PathKind = pathKind,
            ContentKind = LibraryRootContentKind.Unknown
        };

        dbContext.LibraryRoots.Add(root);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRootResponse(root);
    }

    public async Task<LibraryScanResponse> ScanRootAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var root = await dbContext.LibraryRoots.FirstOrDefaultAsync(x => x.Id == rootId, cancellationToken)
            ?? throw new AppNotFoundException("Library root not found.");

        if (!root.IsEnabled)
        {
            throw new AppConflictException("Library root is disabled.");
        }

        if (!Directory.Exists(root.Path))
        {
            throw new AppConflictException($"Library root '{root.Path}' is not reachable.");
        }

        var scan = new LibraryScan
        {
            LibraryRootId = root.Id,
            State = LibraryScanState.Running,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Summary = "Scan queued. Starting shortly..."
        };

        root.LastScanStartedAtUtc = scan.StartedAtUtc;
        root.LastScanCompletedAtUtc = null;
        root.LastScanState = LibraryScanState.Running;
        root.LastScanError = null;
        root.UpdatedAtUtc = DateTimeOffset.UtcNow;

        dbContext.LibraryScans.Add(scan);
        await dbContext.SaveChangesAsync(cancellationToken);

        var scanId = scan.Id;
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ILibraryService>();
            await service.ExecuteScanAsync(scanId, CancellationToken.None);
        });

        return ToScanResponse(scan, root);
    }

    public async Task<LibraryScanResponse?> GetScanAsync(Guid scanId, CancellationToken cancellationToken)
    {
        var scan = await dbContext.LibraryScans
            .Include(x => x.LibraryRoot)
            .FirstOrDefaultAsync(x => x.Id == scanId, cancellationToken);
        return scan is null ? null : ToScanResponse(scan, scan.LibraryRoot!);
    }

    public async Task ExecuteScanAsync(Guid scanId, CancellationToken cancellationToken)
    {
        var scan = await dbContext.LibraryScans
            .Include(x => x.LibraryRoot)
            .FirstOrDefaultAsync(x => x.Id == scanId, cancellationToken);

        if (scan is null || scan.State != LibraryScanState.Running)
        {
            return;
        }

        var root = scan.LibraryRoot!;
        var metadataSettings = await settingsService.GetMetadataRuntimeAsync(cancellationToken);
        var mediaSettings = await settingsService.GetMediaRuntimeAsync(cancellationToken);

        try
        {
            scan.Summary = $"Traversing '{root.Path}'...";
            await dbContext.SaveChangesAsync(cancellationToken);

            var discovered = await DiscoverCandidatesAsync(
                root, scan, metadataSettings, mediaSettings, cancellationToken,
                async (dirs, files, matched, total) =>
                {
                    scan.DirectoriesScanned = dirs;
                    scan.FilesScanned = files;
                    scan.Summary = total > 0
                        ? $"Matching metadata: {matched}/{total} — {dirs} directories, {files} files scanned."
                        : $"Traversing: {dirs} directories, {files} files scanned.";
                    await dbContext.SaveChangesAsync(CancellationToken.None);
                });

            var imported = 0;

            // Save each candidate immediately so CandidatesDetected updates live
            // and a single failure doesn't lose all prior work.
            foreach (var discoveredCandidate in discovered.Candidates)
            {
                var duplicatePackageId = await FindMatchingPackageAsync(discoveredCandidate, cancellationToken);
                var status = duplicatePackageId.HasValue
                    ? LibraryCandidateStatus.Merged
                    : string.Equals(discoveredCandidate.MatchDecision, "AutoImport", StringComparison.OrdinalIgnoreCase)
                        ? mediaSettings.AutoImportHighConfidenceMatches ? LibraryCandidateStatus.AutoImported : LibraryCandidateStatus.PendingReview
                        : LibraryCandidateStatus.PendingReview;

                var candidate = new LibraryCandidate
                {
                    LibraryRootId = root.Id,
                    LibraryScanId = scan.Id,
                    PackageId = duplicatePackageId,
                    Status = status,
                    LocalTitle = discoveredCandidate.LocalTitle,
                    LocalNormalizedTitle = discoveredCandidate.LocalNormalizedTitle,
                    LocalDescription = Truncate(discoveredCandidate.LocalDescription, 2048),
                    Title = discoveredCandidate.Title,
                    NormalizedTitle = discoveredCandidate.NormalizedTitle,
                    Description = Truncate(discoveredCandidate.Description, 2048),
                    Studio = discoveredCandidate.Studio,
                    ReleaseYear = discoveredCandidate.ReleaseYear,
                    CoverImagePath = discoveredCandidate.CoverImagePath,
                    GenresSerialized = string.Join(';', discoveredCandidate.Genres),
                    MetadataProvider = discoveredCandidate.MetadataProvider,
                    MetadataSourceUrl = discoveredCandidate.MetadataSourceUrl,
                    ConfidenceScore = discoveredCandidate.MatchConfidence,
                    MatchDecision = discoveredCandidate.MatchDecision,
                    MatchSummary = Truncate(discoveredCandidate.MatchSummary, 2048),
                    WinningSignalsJson = JsonSerializer.Serialize(discoveredCandidate.WinningSignals, JsonOptions),
                    WarningSignalsJson = JsonSerializer.Serialize(discoveredCandidate.WarningSignals, JsonOptions),
                    ProviderDiagnosticsJson = JsonSerializer.Serialize(discoveredCandidate.ProviderDiagnostics, JsonOptions),
                    AlternativeMatchesJson = JsonSerializer.Serialize(discoveredCandidate.AlternativeMatches, JsonOptions),
                    SelectedMatchKey = discoveredCandidate.SelectedMatchKey,
                    PrimaryPath = discoveredCandidate.PrimaryPath,
                    SourcesJson = JsonSerializer.Serialize(discoveredCandidate.Sources, JsonOptions),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };

                dbContext.LibraryCandidates.Add(candidate);

                if (status == LibraryCandidateStatus.AutoImported)
                {
                    var package = await ImportCandidateAsync(candidate, cancellationToken);
                    candidate.PackageId = package.Id;
                    imported++;
                }

                scan.CandidatesDetected++;
                scan.CandidatesImported = imported;
                scan.Summary = $"Importing: {scan.CandidatesDetected}/{discovered.Candidates.Count} — {discovered.DirectoriesScanned} directories scanned.";
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }

            scan.State = LibraryScanState.Completed;
            scan.DirectoriesScanned = discovered.DirectoriesScanned;
            scan.FilesScanned = discovered.FilesScanned;
            scan.ErrorsCount = 0;
            scan.Summary = $"Scanned {discovered.DirectoriesScanned} directories, found {scan.CandidatesDetected} candidate(s), imported {imported}.";
            scan.CompletedAtUtc = DateTimeOffset.UtcNow;

            root.ContentKind = discovered.ContentKind;
            root.LastScanState = scan.State;
            root.LastScanCompletedAtUtc = scan.CompletedAtUtc;
            root.LastScanError = null;
            root.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Use a nested try so a faulted DbContext cannot silently swallow this failure.
            try
            {
                scan.State = LibraryScanState.Failed;
                scan.CompletedAtUtc = DateTimeOffset.UtcNow;
                scan.ErrorMessage = ex.Message;
                scan.ErrorsCount++;
                scan.Summary = $"Scan failed: {ex.Message}";

                root.LastScanState = LibraryScanState.Failed;
                root.LastScanCompletedAtUtc = scan.CompletedAtUtc;
                root.LastScanError = ex.Message;
                root.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch
            {
                // DbContext is faulted — create a fresh one to record the failure.
                using var errorScope = scopeFactory.CreateScope();
                var errorDb = errorScope.ServiceProvider.GetRequiredService<GamarrDbContext>();
                await errorDb.LibraryScans
                    .Where(s => s.Id == scanId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.State, LibraryScanState.Failed)
                        .SetProperty(x => x.CompletedAtUtc, DateTimeOffset.UtcNow)
                        .SetProperty(x => x.ErrorMessage, ex.Message)
                        .SetProperty(x => x.Summary, $"Scan failed: {ex.Message}"),
                    CancellationToken.None);
                await errorDb.LibraryRoots
                    .Where(r => r.Id == root.Id)
                    .ExecuteUpdateAsync(r => r
                        .SetProperty(x => x.LastScanState, LibraryScanState.Failed)
                        .SetProperty(x => x.LastScanError, ex.Message)
                        .SetProperty(x => x.LastScanCompletedAtUtc, DateTimeOffset.UtcNow)
                        .SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow),
                    CancellationToken.None);
            }
        }
    }

    public async Task<IReadOnlyCollection<LibraryScanResponse>> ListScansAsync(Guid? rootId, LibraryScanState? state, CancellationToken cancellationToken)
    {
        var scans = await dbContext.LibraryScans
            .Include(x => x.LibraryRoot)
            .Where(x => (!rootId.HasValue || x.LibraryRootId == rootId.Value) &&
                        (!state.HasValue || x.State == state.Value))
            .OrderByDescending(x => x.StartedAtUtc)
            .ToListAsync(cancellationToken);

        return scans.Select(x => ToScanResponse(x, x.LibraryRoot!)).ToArray();
    }

    public async Task<LibraryScanResponse> CancelScanAsync(Guid scanId, CancellationToken cancellationToken)
    {
        var scan = await dbContext.LibraryScans
            .Include(x => x.LibraryRoot)
            .FirstOrDefaultAsync(x => x.Id == scanId, cancellationToken)
            ?? throw new AppNotFoundException("Library scan not found.");

        if (scan.State is LibraryScanState.Completed or LibraryScanState.Failed)
        {
            throw new AppConflictException($"Scan is already {scan.State} and cannot be cancelled.");
        }

        scan.State = LibraryScanState.Failed;
        scan.CompletedAtUtc = DateTimeOffset.UtcNow;
        scan.ErrorMessage = "Cancelled by user.";
        scan.Summary = "Scan was cancelled.";

        var root = scan.LibraryRoot!;
        if (root.LastScanState == LibraryScanState.Running)
        {
            root.LastScanState = LibraryScanState.Failed;
            root.LastScanCompletedAtUtc = scan.CompletedAtUtc;
            root.LastScanError = "Cancelled by user.";
            root.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToScanResponse(scan, root);
    }

    public async Task<IReadOnlyCollection<LibraryCandidateResponse>> ListCandidatesAsync(
        LibraryCandidateStatus? status,
        Guid? rootId,
        Guid? scanId,
        string? search,
        CancellationToken cancellationToken)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var candidates = await dbContext.LibraryCandidates
            .Include(x => x.LibraryRoot)
            .Include(x => x.LibraryScan)
            .Where(x => (!status.HasValue || x.Status == status.Value) &&
                        (!rootId.HasValue || x.LibraryRootId == rootId.Value) &&
                        (!scanId.HasValue || x.LibraryScanId == scanId.Value) &&
                        (term == null ||
                         EF.Functions.ILike(x.Title, $"%{term}%") ||
                         EF.Functions.ILike(x.PrimaryPath, $"%{term}%")))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return candidates.Select(ToCandidateResponse).ToArray();
    }

    public async Task<LibraryCandidateResponse> ApproveCandidateAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.LibraryCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new AppNotFoundException("Library candidate not found.");

        if (candidate.PackageId is null)
        {
            var package = await ImportCandidateAsync(candidate, cancellationToken);
            candidate.PackageId = package.Id;
        }

        candidate.Status = LibraryCandidateStatus.Approved;
        candidate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadCandidateResponseAsync(candidate.Id, cancellationToken);
    }

    public async Task<LibraryCandidateResponse> RejectCandidateAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.LibraryCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new AppNotFoundException("Library candidate not found.");

        candidate.Status = LibraryCandidateStatus.Rejected;
        candidate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadCandidateResponseAsync(candidate.Id, cancellationToken);
    }

    public async Task<LibraryCandidateResponse> MergeCandidateAsync(Guid candidateId, MergeLibraryCandidateRequest request, CancellationToken cancellationToken)
    {
        if (request.PackageId == Guid.Empty)
        {
            throw new AppValidationException("PackageId is required.");
        }

        var candidate = await dbContext.LibraryCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new AppNotFoundException("Library candidate not found.");

        var packageExists = await dbContext.Packages.AnyAsync(x => x.Id == request.PackageId, cancellationToken);
        if (!packageExists)
        {
            throw new AppNotFoundException("Package not found.");
        }

        candidate.PackageId = request.PackageId;
        candidate.Status = LibraryCandidateStatus.Merged;
        candidate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadCandidateResponseAsync(candidate.Id, cancellationToken);
    }

    public async Task<LibraryCandidateResponse> UnmergeCandidateAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.LibraryCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new AppNotFoundException("Library candidate not found.");

        candidate.PackageId = null;
        candidate.Status = LibraryCandidateStatus.PendingReview;
        candidate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadCandidateResponseAsync(candidate.Id, cancellationToken);
    }

    public async Task<LibraryCandidateResponse> ReplaceMergeTargetAsync(Guid candidateId, ReplaceMergeTargetRequest request, CancellationToken cancellationToken)
    {
        if (request.PackageId == Guid.Empty)
        {
            throw new AppValidationException("PackageId is required.");
        }

        var candidate = await dbContext.LibraryCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new AppNotFoundException("Library candidate not found.");

        var packageExists = await dbContext.Packages.AnyAsync(x => x.Id == request.PackageId, cancellationToken);
        if (!packageExists)
        {
            throw new AppNotFoundException("Package not found.");
        }

        candidate.PackageId = request.PackageId;
        candidate.Status = LibraryCandidateStatus.Merged;
        candidate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadCandidateResponseAsync(candidate.Id, cancellationToken);
    }

    public async Task<LibraryCandidateResponse> SelectCandidateMatchAsync(Guid candidateId, SelectLibraryCandidateMatchRequest request, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.LibraryCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new AppNotFoundException("Library candidate not found.");

        if (request.LocalOnly)
        {
            ApplyLocalOnlySelection(candidate);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.MatchKey))
            {
                throw new AppValidationException("MatchKey is required when LocalOnly is false.");
            }

            var alternatives = JsonSerializer.Deserialize<List<MetadataMatchOptionResponse>>(candidate.AlternativeMatchesJson, JsonOptions) ?? [];
            var selected = alternatives.FirstOrDefault(x => string.Equals(x.Key, request.MatchKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new AppNotFoundException("Metadata match option not found.");

            ApplySelectedMatch(candidate, selected, alternatives);
        }

        candidate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadCandidateResponseAsync(candidate.Id, cancellationToken);
    }

    public async Task<ManualMetadataSearchResponse> SearchCandidateMetadataAsync(Guid candidateId, ManualMetadataSearchRequest request, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.LibraryCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new AppNotFoundException("Library candidate not found.");

        var group = BuildMutableCandidateGroup(candidate);
        var entrypointHint = group.Sources.OrderBy(s => s.DiscNumber ?? int.MaxValue).FirstOrDefault()?.EntrypointHint;
        return await SearchMetadataAsync(group, request.Query, entrypointHint, cancellationToken);
    }

    public async Task<LibraryCandidateResponse> ApplyCandidateMetadataSearchAsync(Guid candidateId, ApplyManualMetadataMatchRequest request, CancellationToken cancellationToken)
    {
        var candidate = await dbContext.LibraryCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, cancellationToken)
            ?? throw new AppNotFoundException("Library candidate not found.");

        var group = BuildMutableCandidateGroup(candidate);
        var entrypointHint = group.Sources.OrderBy(s => s.DiscNumber ?? int.MaxValue).FirstOrDefault()?.EntrypointHint;
        var search = await SearchMetadataAsync(group, request.Query, entrypointHint, cancellationToken);
        var selected = search.AlternativeMatches.FirstOrDefault(x => string.Equals(x.Key, request.MatchKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new AppNotFoundException("Metadata match option not found.");

        ApplySelectedMatch(candidate, selected, search.AlternativeMatches);
        candidate.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadCandidateResponseAsync(candidate.Id, cancellationToken);
    }

    public async Task<BulkReMatchResponse> BulkReMatchAsync(CancellationToken cancellationToken)
    {
        var packages = await QueryPackages(includeArchived: false)
            .Where(p => p.MetadataSelectionKind == "LocalOnly" || p.MetadataSelectionKind == "Unknown" || p.MetadataSelectionKind == null)
            .ToListAsync(cancellationToken);

        var metadataSettings = await settingsService.GetMetadataRuntimeAsync(cancellationToken);
        var processedCount = 0;
        var autoImportedCount = 0;
        var nowReviewableCount = 0;
        var stillUnmatchedCount = 0;

        foreach (var package in packages)
        {
            processedCount++;
            var version = package.Versions.OrderByDescending(v => v.IsActive).First();
            var localGroup = BuildMutableCandidateGroup(version);
            var entrypointHint = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).FirstOrDefault()?.EntrypointHint;
            var metadataSearch = await SearchWithFallbackAsync(localGroup.Title, entrypointHint, cancellationToken);
            var evaluation = EvaluateMetadata(localGroup, metadataSearch, metadataSettings);

            if (evaluation.SelectedMatch is not null &&
                (string.Equals(evaluation.Decision, "AutoImport", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(evaluation.Decision, "Review", StringComparison.OrdinalIgnoreCase)))
            {
                var slug = await EnsureUniqueSlugForUpdateAsync(package.Id, NormalizeTitle(evaluation.SelectedMatch.Title), cancellationToken);
                var firstAlt = evaluation.AlternativeMatches.FirstOrDefault(m => string.Equals(m.Key, evaluation.SelectedMatch.Key, StringComparison.OrdinalIgnoreCase))
                               ?? evaluation.AlternativeMatches.FirstOrDefault();
                if (firstAlt is not null)
                {
                    var updateRequest = BuildSelectedMetadataUpdate(package, localGroup, firstAlt, slug);
                    await packageService.UpdateMetadataAsync(package.Id, updateRequest, cancellationToken);

                    if (string.Equals(evaluation.Decision, "AutoImport", StringComparison.OrdinalIgnoreCase))
                        autoImportedCount++;
                    else
                        nowReviewableCount++;
                }
                else
                {
                    stillUnmatchedCount++;
                }
            }
            else
            {
                stillUnmatchedCount++;
            }
        }

        return new BulkReMatchResponse(processedCount, autoImportedCount, nowReviewableCount, stillUnmatchedCount);
    }

    private static void ClearDirectoryContents(string rootPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!CanSafelyClearDirectory(normalizedRoot))
        {
            throw new AppValidationException($"Refusing to clear unsafe normalized asset root '{normalizedRoot}'.");
        }

        foreach (var directory in Directory.GetDirectories(normalizedRoot))
        {
            Directory.Delete(directory, recursive: true);
        }

        foreach (var file in Directory.GetFiles(normalizedRoot))
        {
            File.Delete(file);
        }
    }

    private static bool CanSafelyClearDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var root = Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        return !string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> CountMachineMountsSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.MachineMounts.CountAsync(cancellationToken);
        }
        catch (Exception ex) when (IsUndefinedTable(ex))
        {
            return 0;
        }
    }

    private async Task DeleteMachineMountsSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.MachineMounts.ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex) when (IsUndefinedTable(ex))
        {
        }
    }

    private static bool IsUndefinedTable(Exception ex) =>
        ex is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UndefinedTable ||
        ex.InnerException is not null && IsUndefinedTable(ex.InnerException);

    private IQueryable<Package> QueryPackages(bool includeArchived = false) =>
        dbContext.Packages
            .Where(p => includeArchived || !p.IsArchived)
            .Include(p => p.Versions).ThenInclude(v => v.Media)
            .Include(p => p.Versions).ThenInclude(v => v.DetectionRules)
            .Include(p => p.Versions).ThenInclude(v => v.Prerequisites);

    private LibraryTitleResponse ToLibraryResponse(Package package, Guid? machineId)
    {
        var version = package.Versions.OrderByDescending(v => v.IsActive).First();
        var machineInstall = machineId.HasValue ? FindMachineInstall(version.Id, machineId.Value) : null;
        var installState = machineId.HasValue ? ResolveInstallState(package.Id, machineId.Value, version.Id, machineInstall) : LibraryInstallState.NotInstalled;
        var latestJob = machineId.HasValue
            ? dbContext.Jobs.Where(j => j.PackageId == package.Id && j.MachineId == machineId.Value)
                .OrderByDescending(j => j.CreatedAtUtc)
                .Select(j => new { j.Id, j.State, j.ActionType, j.CreatedAtUtc })
                .FirstOrDefault()
            : null;
        var installStrategy = string.IsNullOrWhiteSpace(version.InstallStrategy) ? ResolveInstallStrategy(version.InstallScriptPath) : version.InstallStrategy;
        var recipeDiagnostics = string.IsNullOrWhiteSpace(version.InstallDiagnostics) ? (string.IsNullOrWhiteSpace(version.Notes) ? "No install diagnostics." : version.Notes) : version.InstallDiagnostics;
        var supportedInstallPath = ResolveSupportedInstallPath(version, installStrategy);
        var sourceHealth = ResolveSourceHealth(version);
        var reviewRequiredReason = ResolveReviewRequiredReason(version, sourceHealth);
        var metadataStatus = ResolvePackageMetadataStatus(package);
        var metadataPrimarySource = ResolveMetadataPrimarySource(package.MetadataProvider);
        var metadataConfidence = ResolveMetadataConfidence(package);
        var canPlay = installState == LibraryInstallState.Installed && HasLaunchCapability(version);
        var installReadiness = ResolveInstallReadiness(supportedInstallPath, sourceHealth, reviewRequiredReason);
        var playReadiness = ResolvePlayReadiness(installState, version);
        var isInstallStateStale = machineId.HasValue && ShouldRefreshInstalledState(machineInstall);
        var isInstallable = string.Equals(installReadiness, "Ready", StringComparison.OrdinalIgnoreCase);
        var canInstall = !canPlay &&
                         installState != LibraryInstallState.Installing &&
                         installState != LibraryInstallState.Uninstalling &&
                         string.Equals(installReadiness, "Ready", StringComparison.OrdinalIgnoreCase);
        var canValidate = machineId.HasValue &&
                          installState != LibraryInstallState.Installing &&
                          installState != LibraryInstallState.Uninstalling;
        var canUninstall = machineId.HasValue &&
                           installState == LibraryInstallState.Installed &&
                           HasUninstallCapability(version, machineInstall);
        var sourceConflictCount = FindSourceConflicts(package.Id, version.Media.Select(m => m.Path)).Count;
        var posterImageUrl = ResolvePosterImageUrl(package);
        var backdropImageUrl = ResolveBackdropImageUrl(package);
        var storeDescription = ResolveStoreDescription(package);

        return new LibraryTitleResponse(
            package.Id,
            package.Slug,
            package.Name,
            package.Description,
            package.Notes,
            package.Tags.ToArray(),
            package.Genres.ToArray(),
            package.Studio,
            package.ReleaseYear,
            package.CoverImagePath,
            version.VersionLabel,
            version.InstallScriptKind,
            version.LaunchExecutablePath,
            installState,
            machineInstall?.LastValidatedAtUtc,
            isInstallStateStale,
            machineInstall?.ValidationSummary,
            canValidate,
            canUninstall,
            latestJob?.Id,
            latestJob?.State,
            latestJob?.ActionType,
            latestJob?.CreatedAtUtc,
            BuildSourceSummary(version),
            sourceHealth,
            sourceConflictCount,
            installStrategy,
            version.ProcessingState,
            supportedInstallPath,
            installReadiness,
            playReadiness,
            isInstallable,
            canInstall,
            canPlay,
            reviewRequiredReason,
            recipeDiagnostics,
            version.NormalizationDiagnostics,
            version.NormalizedAssetRootPath,
            version.NormalizedAtUtc,
            package.MetadataProvider,
            package.MetadataSourceUrl,
            package.MetadataSelectionKind,
            metadataStatus,
            metadataPrimarySource,
            metadataConfidence,
            posterImageUrl,
            backdropImageUrl,
            storeDescription,
            package.IsArchived,
            package.ArchivedReason,
            package.ArchivedAtUtc,
            package.CreatedAtUtc,
            package.UpdatedAtUtc);
    }

    private static LibraryRootResponse ToRootResponse(LibraryRoot root)
    {
        var isReachable = Directory.Exists(root.Path);
        var healthSummary = isReachable
            ? "Reachable"
            : root.LastScanState == LibraryScanState.Failed && !string.IsNullOrWhiteSpace(root.LastScanError)
                ? root.LastScanError!
                : "Unavailable";

        return new(
            root.Id,
            root.DisplayName,
            root.Path,
            root.PathKind,
            root.ContentKind,
            root.IsEnabled,
            root.CreatedAtUtc,
            root.UpdatedAtUtc,
            root.LastScanStartedAtUtc,
            root.LastScanCompletedAtUtc,
            root.LastScanState,
            root.LastScanError,
            isReachable,
            healthSummary);
    }

    private static LibraryScanResponse ToScanResponse(LibraryScan scan, LibraryRoot root) =>
        new(
            scan.Id,
            scan.LibraryRootId,
            root.DisplayName,
            root.Path,
            scan.State,
            scan.DirectoriesScanned,
            scan.FilesScanned,
            scan.CandidatesDetected,
            scan.CandidatesImported,
            scan.ErrorsCount,
            scan.Summary,
            scan.ErrorMessage,
            scan.StartedAtUtc,
            scan.CompletedAtUtc);

    private LibraryCandidateResponse ToCandidateResponse(LibraryCandidate candidate)
    {
        var sources = JsonSerializer.Deserialize<List<LibraryCandidateSourceResponse>>(candidate.SourcesJson, JsonOptions) ?? [];
        var installPlan = BuildInstallPlan(candidate, sources);
        var winningSignals = JsonSerializer.Deserialize<List<string>>(candidate.WinningSignalsJson, JsonOptions) ?? [];
        var warningSignals = JsonSerializer.Deserialize<List<string>>(candidate.WarningSignalsJson, JsonOptions) ?? [];
        var providerDiagnostics = JsonSerializer.Deserialize<List<ProviderDiagnosticResponse>>(candidate.ProviderDiagnosticsJson, JsonOptions) ?? [];
        var alternativeMatches = JsonSerializer.Deserialize<List<MetadataMatchOptionResponse>>(candidate.AlternativeMatchesJson, JsonOptions) ?? [];
        var selectedMatch = alternativeMatches.FirstOrDefault(x => string.Equals(x.Key, candidate.SelectedMatchKey, StringComparison.OrdinalIgnoreCase))
                            ?? alternativeMatches.FirstOrDefault(x => string.Equals(x.Provider, candidate.MetadataProvider, StringComparison.OrdinalIgnoreCase) &&
                                                                      string.Equals(x.Title, candidate.Title, StringComparison.OrdinalIgnoreCase));
        var sourceConflicts = FindSourceConflicts(candidate.PackageId, sources.Select(x => x.Path));
        return new LibraryCandidateResponse(
            candidate.Id,
            candidate.LibraryRootId,
            candidate.LibraryScanId,
            candidate.PackageId,
            candidate.LibraryRoot?.DisplayName ?? string.Empty,
            candidate.LibraryScan?.StartedAtUtc,
            candidate.Status,
            candidate.Title,
            candidate.Description,
            candidate.Studio,
            candidate.ReleaseYear,
            candidate.CoverImagePath,
            candidate.Genres.ToArray(),
            candidate.MetadataProvider,
            candidate.MetadataSourceUrl,
            candidate.ConfidenceScore,
            ResolveCandidateMetadataStatus(candidate),
            ResolveMetadataPrimarySource(candidate.MetadataProvider),
            selectedMatch?.CoverImagePath ?? candidate.CoverImagePath,
            selectedMatch?.BackdropImageUrl,
            ResolveStoreDescription(candidate.Title, candidate.Description),
            candidate.PrimaryPath,
            sources.Count,
            sources.Any(source => source.HintFilePresent),
            installPlan.Strategy,
            !string.Equals(installPlan.InstallScriptPath, "builtin:needs-review", StringComparison.OrdinalIgnoreCase),
            installPlan.Diagnostics,
            candidate.MatchDecision,
            candidate.MatchSummary,
            winningSignals,
            warningSignals,
            providerDiagnostics,
            alternativeMatches,
            candidate.SelectedMatchKey,
            sourceConflicts,
            sources,
            candidate.CreatedAtUtc,
            candidate.UpdatedAtUtc);
    }

    private async Task<LibraryCandidateResponse> LoadCandidateResponseAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var loaded = await dbContext.LibraryCandidates
            .Include(x => x.LibraryRoot)
            .Include(x => x.LibraryScan)
            .FirstAsync(x => x.Id == candidateId, cancellationToken);

        return ToCandidateResponse(loaded);
    }

    private static LibraryCandidateSourceResponse ToCandidateSourceResponse(PackageMedia media) =>
        new(media.Label, media.Path, media.MediaType, media.SourceKind, media.ScratchPolicy, media.DiscNumber, media.EntrypointHint, false);

    private static string BuildSourceSummary(PackageVersion version)
    {
        if (string.Equals(version.ProcessingState, "Ready", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(version.NormalizedAssetRootPath))
        {
            return $"Library folder • {Path.GetFileName(version.NormalizedAssetRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
        }

        var media = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).ToArray();
        if (media.Length == 0)
        {
            return "No sources";
        }

        if (media.Length == 1)
        {
            return $"{media[0].MediaType} • {Path.GetFileName(media[0].Path)}";
        }

        return $"{media.Length} sources • {string.Join(", ", media.Take(2).Select(m => Path.GetFileName(m.Path)))}";
    }

    private static string ResolveSourceHealth(PackageVersion version)
    {
        if (string.Equals(version.ProcessingState, "Normalizing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(version.ProcessingState, "Discovered", StringComparison.OrdinalIgnoreCase))
        {
            return "Preparing";
        }

        if (string.Equals(version.ProcessingState, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Failed";
        }

        if (string.Equals(version.ProcessingState, "Ready", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(version.NormalizedAssetRootPath))
        {
            return Directory.Exists(version.NormalizedAssetRootPath) ? "Available" : "Missing";
        }

        if (!version.Media.Any())
        {
            return "Missing";
        }

        var hasAvailableSource = version.Media.Any(m => File.Exists(m.Path) || Directory.Exists(m.Path));
        return hasAvailableSource ? "Available" : "Missing";
    }

    private static string ResolveInstallStrategy(string installScriptPath) =>
        installScriptPath.ToLowerInvariant() switch
        {
            "builtin:portable-copy" or "builtin:library-import" => "PortableCopy",
            "builtin:auto-install" or "builtin:needs-review" => "AutoInstall",
            _ => "Custom"
        };

    private static bool HasLaunchCapability(PackageVersion version) =>
        !string.IsNullOrWhiteSpace(version.LaunchExecutablePath) ||
        version.DetectionRules.Any(rule =>
            string.Equals(rule.RuleType, "FileExists", StringComparison.OrdinalIgnoreCase) &&
            !IsInstallMarkerPath(rule.Value)) ||
        CanDiscoverInstalledLaunchPath(version);

    private static bool IsInstallMarkerPath(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("gamarr-library-import.marker", StringComparison.OrdinalIgnoreCase);

    private static bool CanDiscoverInstalledLaunchPath(PackageVersion version) =>
        string.Equals(version.InstallStrategy, "AutoInstall", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(version.InstallerFamily, "Msi", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(version.InstallerFamily, "Inno", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(version.InstallerFamily, "Nsis", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(version.InstallerFamily, "InstallShield", StringComparison.OrdinalIgnoreCase));

    private static string? ResolveReviewRequiredReason(PackageVersion version, string sourceHealth)
    {
        if (string.Equals(version.ProcessingState, "Normalizing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(version.ProcessingState, "Discovered", StringComparison.OrdinalIgnoreCase))
        {
            return "This title is still being analyzed so Gamarr can use the library source in place.";
        }

        if (string.Equals(version.ProcessingState, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(version.NormalizationDiagnostics)
                ? "Normalization failed before an installable package could be built."
                : version.NormalizationDiagnostics;
        }

        if (string.Equals(sourceHealth, "Missing", StringComparison.OrdinalIgnoreCase))
        {
            return "Source media is missing or unreachable.";
        }

        return null;
    }

    private static string ResolveSupportedInstallPath(PackageVersion version, string installStrategy)
    {
        if (string.Equals(installStrategy, "PortableCopy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(installStrategy, "PortableDirect", StringComparison.OrdinalIgnoreCase))
        {
            return "Portable";
        }

        if (string.Equals(installStrategy, "AutoInstall", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(version.InstallerFamily, "Msi", StringComparison.OrdinalIgnoreCase))
        {
            return "Msi";
        }

        if (string.Equals(installStrategy, "AutoInstall", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(version.InstallerFamily, "Inno", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(version.InstallerFamily, "Nsis", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(version.InstallerFamily, "InstallShield", StringComparison.OrdinalIgnoreCase)))
        {
            return version.InstallerFamily;
        }

        // AutoInstall with Unknown family — agent will detect at mount time
        if (string.Equals(installStrategy, "AutoInstall", StringComparison.OrdinalIgnoreCase))
        {
            return "AutoInstall";
        }

        var hasManualDefinition =
            !version.InstallScriptPath.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(version.InstallerPath) &&
             !string.IsNullOrWhiteSpace(version.LaunchExecutablePath) &&
             version.DetectionRules.Count > 0);

        return hasManualDefinition ? "Manual" : "Unsupported";
    }

    private static string ResolveInstallReadiness(string supportedInstallPath, string sourceHealth, string? reviewRequiredReason)
    {
        if (string.Equals(sourceHealth, "Preparing", StringComparison.OrdinalIgnoreCase))
        {
            return "Preparing";
        }

        if (string.Equals(sourceHealth, "Missing", StringComparison.OrdinalIgnoreCase))
        {
            return "MissingSource";
        }

        if (!string.IsNullOrWhiteSpace(reviewRequiredReason) ||
            string.Equals(supportedInstallPath, "Unsupported", StringComparison.OrdinalIgnoreCase))
        {
            return "ReviewRequired";
        }

        return "Ready";
    }

    private static string ResolvePlayReadiness(LibraryInstallState installState, PackageVersion version)
    {
        if (installState != LibraryInstallState.Installed)
        {
            return "NotInstalled";
        }

        return HasLaunchCapability(version) ? "Ready" : "InvalidLaunch";
    }

    private static string ResolvePackageMetadataStatus(Package package)
    {
        if (string.Equals(package.MetadataSelectionKind, "LocalOnly", StringComparison.OrdinalIgnoreCase))
        {
            return "Forced Local Only";
        }

        if (!string.IsNullOrWhiteSpace(package.MetadataProvider) &&
            string.Equals(package.MetadataSelectionKind, "ProviderMatch", StringComparison.OrdinalIgnoreCase))
        {
            return $"{package.MetadataProvider} Match";
        }

        if (!string.IsNullOrWhiteSpace(package.MetadataProvider))
        {
            return "Remote Match Needs Review";
        }

        return "No Remote Match";
    }

    private static string ResolveCandidateMetadataStatus(LibraryCandidate candidate)
    {
        if (string.Equals(candidate.MatchDecision, "AutoImport", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(candidate.MetadataProvider))
        {
            return $"{candidate.MetadataProvider} Match";
        }

        if (string.Equals(candidate.MatchDecision, "Review", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(candidate.MetadataProvider))
        {
            return "Remote Match Needs Review";
        }

        if (string.Equals(candidate.MatchDecision, "LocalOnly", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(candidate.MetadataProvider) ? "No Remote Match" : "Forced Local Only";
        }

        return string.IsNullOrWhiteSpace(candidate.MetadataProvider) ? "No Remote Match" : $"{candidate.MetadataProvider} Match";
    }

    private static string? ResolveMetadataPrimarySource(string? metadataProvider) =>
        string.IsNullOrWhiteSpace(metadataProvider) ? null : metadataProvider.Trim();

    private static double? ResolveMetadataConfidence(Package package) =>
        string.IsNullOrWhiteSpace(package.MetadataProvider) ? null : 0.95d;

    private static string? ResolvePosterImageUrl(Package package) =>
        string.IsNullOrWhiteSpace(package.CoverImagePath) ? null : package.CoverImagePath.Trim();

    private static string? ResolveBackdropImageUrl(Package package)
    {
        if (string.IsNullOrWhiteSpace(package.CoverImagePath))
        {
            return null;
        }

        return package.CoverImagePath.Contains("header", StringComparison.OrdinalIgnoreCase) ||
               package.CoverImagePath.Contains("background", StringComparison.OrdinalIgnoreCase)
            ? package.CoverImagePath
            : null;
    }

    private static string ResolveStoreDescription(Package package) =>
        ResolveStoreDescription(package.Name, package.Description);

    private static string ResolveStoreDescription(string title, string? description)
    {
        var normalized = WebUtility.HtmlDecode(description ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return $"Owned PC media title managed by Gamarr. Metadata and install readiness can be refined from the import and package review flows.";
        }

        return normalized;
    }

    private LibraryInstallState ResolveInstallState(Guid packageId, Guid machineId, Guid? packageVersionId = null, MachinePackageInstall? machineInstall = null)
    {
        var active = dbContext.Jobs
            .Where(j => j.PackageId == packageId && j.MachineId == machineId)
            .Where(j =>
                j.State == JobState.Queued ||
                j.State == JobState.Assigned ||
                j.State == JobState.Preparing ||
                j.State == JobState.Mounting ||
                j.State == JobState.Installing ||
                j.State == JobState.Validating)
            .Select(j => new { j.ActionType, j.State })
            .OrderByDescending(j => j.ActionType == JobActionType.Install ? 1 : 0)
            .ThenByDescending(j => j.State == JobState.Completed ? 1 : 0)
            .FirstOrDefault();
        if (active is not null)
        {
            return active.ActionType == JobActionType.Uninstall
                ? LibraryInstallState.Uninstalling
                : LibraryInstallState.Installing;
        }

        machineInstall ??= packageVersionId.HasValue ? FindMachineInstall(packageVersionId.Value, machineId) : null;
        if (machineInstall is not null)
        {
            return string.Equals(machineInstall.State, "Installed", StringComparison.OrdinalIgnoreCase)
                ? LibraryInstallState.Installed
                : LibraryInstallState.NotInstalled;
        }

        var latestInstall = dbContext.Jobs
            .Where(j => j.PackageId == packageId && j.MachineId == machineId && j.ActionType == JobActionType.Install)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Select(j => new { j.State })
            .FirstOrDefault();

        if (latestInstall is null)
        {
            return LibraryInstallState.NotInstalled;
        }

        if (latestInstall.State == JobState.Completed)
        {
            return LibraryInstallState.Installed;
        }

        if (latestInstall.State == JobState.Failed)
        {
            return LibraryInstallState.Failed;
        }

        return LibraryInstallState.NotInstalled;
    }

    private MachinePackageInstall? FindMachineInstall(Guid packageVersionId, Guid machineId) =>
        dbContext.MachinePackageInstalls
            .AsNoTracking()
            .FirstOrDefault(x => x.MachineId == machineId && x.PackageVersionId == packageVersionId);

    private static bool ShouldRefreshInstalledState(MachinePackageInstall? machineInstall)
    {
        if (machineInstall is null)
        {
            return true;
        }

        if (!string.Equals(machineInstall.State, "Installed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return machineInstall.LastValidatedAtUtc is null ||
               DateTimeOffset.UtcNow - machineInstall.LastValidatedAtUtc.Value > TimeSpan.FromMinutes(10);
    }

    private static bool HasUninstallCapability(PackageVersion version, MachinePackageInstall? machineInstall = null) =>
        !string.IsNullOrWhiteSpace(version.UninstallScriptPath) ||
        !string.IsNullOrWhiteSpace(machineInstall?.ResolvedUninstallCommand) ||
        version.DetectionRules.Any(rule => string.Equals(rule.RuleType, "UninstallEntryExists", StringComparison.OrdinalIgnoreCase));

    private async Task<Guid?> FindMatchingPackageAsync(DiscoveredCandidate candidate, CancellationToken cancellationToken)
    {
        var sourcePaths = candidate.Sources.Select(x => x.Path).ToArray();
        var normalizedTitle = string.IsNullOrWhiteSpace(candidate.LocalNormalizedTitle) ? candidate.NormalizedTitle : candidate.LocalNormalizedTitle;
        return await dbContext.Packages
            .Where(p => p.Slug == normalizedTitle ||
                        p.Versions.SelectMany(v => v.Media).Any(m => sourcePaths.Contains(m.Path)))
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PackageResponse> ImportCandidateAsync(LibraryCandidate candidate, CancellationToken cancellationToken)
    {
        var sources = JsonSerializer.Deserialize<List<LibraryCandidateSourceResponse>>(candidate.SourcesJson, JsonOptions) ?? [];
        if (sources.Count == 0)
        {
            throw new AppConflictException("Library candidate has no importable sources.");
        }

        string? tidySummary = null;
        if (ShouldAutoTidy(candidate))
        {
            (sources, tidySummary) = await TryApplyFolderTidyAsync(candidate, sources, candidate.Title, cancellationToken);
        }

        var normalizedSlug = candidate.NormalizedTitle;
        var uniqueSlug = await EnsureUniqueSlugAsync(normalizedSlug, cancellationToken);
        var installPlan = BuildInstallPlan(candidate, sources);

        var rawDescription = string.IsNullOrWhiteSpace(candidate.Description)
            ? $"Imported from library root scan at {candidate.PrimaryPath}."
            : candidate.Description;
        var rawNotes = $"Imported from library scan on {candidate.CreatedAtUtc:yyyy-MM-dd}. Source path: {candidate.PrimaryPath}{Environment.NewLine}Install strategy: {installPlan.Strategy}.{Environment.NewLine}{candidate.MatchSummary}{Environment.NewLine}{installPlan.Diagnostics}";
        if (!string.IsNullOrWhiteSpace(tidySummary))
        {
            rawNotes += $"{Environment.NewLine}{tidySummary}";
        }

        var request = new CreatePackageRequest(
            uniqueSlug,
            candidate.Title,
            Truncate(rawDescription, 2048),
            Truncate(rawNotes, 4096),
            new[] { "scanned", "library-import", installPlan.Tag },
            candidate.Genres.Any() ? candidate.Genres.ToArray() : ["Unknown"],
            candidate.Studio,
            candidate.ReleaseYear,
            candidate.CoverImagePath,
            new CreatePackageVersionRequest(
                "1.0",
                "Windows 10, Windows 11",
                ArchitectureKind.X64,
                InstallScriptKind.PowerShell,
                installPlan.InstallScriptPath,
                null,
                null,
                3600,
                installPlan.Diagnostics,
                installPlan.Strategy,
                installPlan.InstallerFamily,
                installPlan.InstallerPath,
                installPlan.SilentArguments,
                installPlan.Diagnostics,
                installPlan.LaunchExecutablePath,
                sources.Select(x => new CreatePackageMediaRequest(x.MediaType, x.Label, x.Path, x.DiscNumber, x.EntrypointHint, x.SourceKind, x.ScratchPolicy)).ToArray(),
                installPlan.DetectionRules,
                Array.Empty<CreatePrerequisiteRequest>()),
            candidate.MetadataProvider,
            candidate.MetadataSourceUrl,
            candidate.MatchDecision == "LocalOnly" ? "LocalOnly" : string.IsNullOrWhiteSpace(candidate.MetadataProvider) ? "Unknown" : "ProviderMatch");

        return await packageService.CreateAsync(request, cancellationToken);
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug, CancellationToken cancellationToken)
    {
        var candidate = string.IsNullOrWhiteSpace(baseSlug) ? "imported-title" : baseSlug;
        if (!await dbContext.Packages.AnyAsync(x => x.Slug == candidate, cancellationToken))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var attempt = $"{candidate}-{suffix}";
            if (!await dbContext.Packages.AnyAsync(x => x.Slug == attempt, cancellationToken))
            {
                return attempt;
            }
        }

        throw new AppConflictException($"Unable to allocate a unique slug for '{baseSlug}'.");
    }

    private async Task<string> EnsureUniqueSlugForUpdateAsync(Guid packageId, string baseSlug, CancellationToken cancellationToken)
    {
        var candidate = string.IsNullOrWhiteSpace(baseSlug) ? "imported-title" : baseSlug;
        if (!await dbContext.Packages.AnyAsync(x => x.Id != packageId && x.Slug == candidate, cancellationToken))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var attempt = $"{candidate}-{suffix}";
            if (!await dbContext.Packages.AnyAsync(x => x.Id != packageId && x.Slug == attempt, cancellationToken))
            {
                return attempt;
            }
        }

        throw new AppConflictException($"Unable to allocate a unique slug for '{baseSlug}'.");
    }

    private MutableCandidateGroup BuildMutableCandidateGroup(PackageVersion version)
    {
        var orderedMedia = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).ThenBy(m => m.Label).ToArray();
        var primary = orderedMedia.First();
        var localTitle = DeriveLocalTitleFromPath(primary.Path);
        var localDescription = primary.MediaType is MediaType.Iso or MediaType.DiskImage
            ? $"Disc image set discovered under {Path.GetDirectoryName(primary.Path) ?? primary.Path}."
            : $"Folder candidate detected at {primary.Path}.";

        return new MutableCandidateGroup(
            localTitle,
            NormalizeTitle(localTitle),
            primary.Path,
            localDescription,
            orderedMedia.Select(ToCandidateSourceResponse).ToList());
    }

    private static MutableCandidateGroup BuildMutableCandidateGroup(LibraryCandidate candidate)
    {
        var sources = JsonSerializer.Deserialize<List<LibraryCandidateSourceResponse>>(candidate.SourcesJson, JsonOptions) ?? [];
        return new MutableCandidateGroup(
            string.IsNullOrWhiteSpace(candidate.LocalTitle) ? candidate.Title : candidate.LocalTitle,
            string.IsNullOrWhiteSpace(candidate.LocalNormalizedTitle) ? NormalizeTitle(candidate.Title) : candidate.LocalNormalizedTitle,
            candidate.PrimaryPath,
            string.IsNullOrWhiteSpace(candidate.LocalDescription) ? candidate.Description : candidate.LocalDescription,
            sources);
    }

    private async Task<IReadOnlyCollection<LibrarySourceConflictResponse>> FindSourceConflictsAsync(Guid? currentPackageId, IEnumerable<string> sourcePaths, CancellationToken cancellationToken)
    {
        var packageSummaries = await dbContext.Packages
            .Include(p => p.Versions)
                .ThenInclude(v => v.Media)
            .ToListAsync(cancellationToken);
        return FindSourceConflicts(currentPackageId, sourcePaths, packageSummaries);
    }

    private IReadOnlyCollection<LibrarySourceConflictResponse> FindSourceConflicts(Guid? currentPackageId, IEnumerable<string> sourcePaths) =>
        FindSourceConflicts(currentPackageId, sourcePaths, QueryPackages().ToList());

    private static IReadOnlyCollection<LibrarySourceConflictResponse> FindSourceConflicts(Guid? currentPackageId, IEnumerable<string> sourcePaths, IReadOnlyCollection<Package> packages)
    {
        var normalizedSources = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedSources.Length == 0)
        {
            return Array.Empty<LibrarySourceConflictResponse>();
        }

        var conflicts = new List<LibrarySourceConflictResponse>();
        foreach (var package in packages.Where(p => !currentPackageId.HasValue || p.Id != currentPackageId.Value))
        {
            var mediaPaths = package.Versions.SelectMany(v => v.Media).Select(m => m.Path).ToArray();
            foreach (var source in normalizedSources)
            {
                foreach (var other in mediaPaths)
                {
                    var normalizedOther = Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(source, normalizedOther, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts.Add(new LibrarySourceConflictResponse("ExactPath", normalizedOther, package.Id, package.Name));
                        continue;
                    }

                    var sourceRoot = GetConflictRoot(source);
                    var otherRoot = GetConflictRoot(normalizedOther);
                    if (string.Equals(sourceRoot, otherRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts.Add(new LibrarySourceConflictResponse("SharedRoot", otherRoot, package.Id, package.Name));
                    }
                }
            }
        }

        return conflicts
            .DistinctBy(x => $"{x.ConflictType}|{x.Path}|{x.PackageId}")
            .ToArray();
    }

    private static string GetConflictRoot(string path)
    {
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? path;
    }

    private static string DeriveLocalTitleFromPath(string path)
    {
        if (Directory.Exists(path))
        {
            return CleanTitle(Path.GetFileName(path));
        }

        var directory = Path.GetDirectoryName(path);
        var containerName = string.IsNullOrWhiteSpace(directory) ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(directory);
        return CleanTitle(containerName);
    }

    private static string BuildLocalDescriptionForPath(string path) =>
        Directory.Exists(path)
            ? $"Folder candidate detected at {path}."
            : $"Disc image set discovered under {Path.GetDirectoryName(path) ?? path}.";

    private static string? ResolveTidyContainerPath(string rootPath, IReadOnlyCollection<LibraryCandidateSourceResponse> sources)
    {
        var rootFullPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var containerPaths = sources
            .Select(source =>
            {
                if (source.SourceKind == PackageSourceKind.DirectFolder || Directory.Exists(source.Path))
                {
                    return Path.GetFullPath(source.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }

                var directory = Path.GetDirectoryName(source.Path);
                return string.IsNullOrWhiteSpace(directory)
                    ? null
                    : Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (containerPaths.Length != 1)
        {
            return null;
        }

        var containerPath = containerPaths[0]!;
        return string.Equals(containerPath, rootFullPath, StringComparison.OrdinalIgnoreCase) ? null : containerPath;
    }

    private static string BuildCanonicalFolderName(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var safe = title.Trim().Replace(':', '-');
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, ' ');
        }

        safe = Regex.Replace(safe, @"\s+", " ").Trim().TrimEnd('.', ' ');
        return safe;
    }

    private static bool IsPathUnderRoot(string path, string rootPath)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReplacePathPrefix(string path, string oldPrefix, string newPrefix)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedOld = Path.GetFullPath(oldPrefix).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedNew = Path.GetFullPath(newPrefix).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedPath, normalizedOld, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedNew;
        }

        var prefix = normalizedOld + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Path.Combine(normalizedNew, normalizedPath[prefix.Length..]);
    }

    private static UpdatePackageMetadataRequest BuildLocalOnlyMetadataUpdate(Package package, MutableCandidateGroup localGroup, string slug) =>
        new(
            slug,
            localGroup.Title,
            localGroup.Description,
            package.Notes,
            package.Tags.ToArray().Concat(["reconciled", "local-only"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            package.Genres.Count == 0 ? Array.Empty<string>() : package.Genres.ToArray(),
            string.Empty,
            null,
            null,
            null,
            null,
            "LocalOnly");

    private static UpdatePackageMetadataRequest BuildSelectedMetadataUpdate(Package package, MutableCandidateGroup localGroup, MetadataMatchOptionResponse selected, string slug) =>
        new(
            slug,
            selected.Title,
            string.IsNullOrWhiteSpace(selected.Description) ? localGroup.Description : selected.Description,
            package.Notes,
            package.Tags.ToArray().Concat(["reconciled"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            selected.Genres.Any() ? selected.Genres.ToArray() : package.Genres.ToArray(),
            selected.Studio,
            selected.ReleaseYear,
            selected.CoverImagePath,
            selected.Provider,
            selected.SourceUrl,
            "ProviderMatch");

    private ImportInstallPlan BuildInstallPlan(Package package, IReadOnlyCollection<LibraryCandidateSourceResponse> sources)
    {
        var candidate = new LibraryCandidate
        {
            Title = package.Name,
            PrimaryPath = sources.FirstOrDefault()?.Path ?? string.Empty
        };

        return BuildInstallPlan(candidate, sources);
    }

    private static void ApplyLocalOnlySelection(LibraryCandidate candidate)
    {
        candidate.Title = string.IsNullOrWhiteSpace(candidate.LocalTitle) ? candidate.Title : candidate.LocalTitle;
        candidate.NormalizedTitle = string.IsNullOrWhiteSpace(candidate.LocalNormalizedTitle) ? NormalizeTitle(candidate.Title) : candidate.LocalNormalizedTitle;
        candidate.Description = string.IsNullOrWhiteSpace(candidate.LocalDescription) ? candidate.Description : candidate.LocalDescription;
        candidate.Studio = string.Empty;
        candidate.ReleaseYear = null;
        candidate.CoverImagePath = null;
        candidate.GenresSerialized = string.Empty;
        candidate.MetadataProvider = null;
        candidate.MetadataSourceUrl = null;
        candidate.ConfidenceScore = 0.55d;
        candidate.MatchDecision = "LocalOnly";
        candidate.MatchSummary = "Manual override selected local-only import. Provider metadata will not be used.";
        candidate.WinningSignalsJson = JsonSerializer.Serialize(new[] { "Manual local-only override selected." }, JsonOptions);
        candidate.WarningSignalsJson = JsonSerializer.Serialize(Array.Empty<string>(), JsonOptions);
        candidate.ProviderDiagnosticsJson = JsonSerializer.Serialize(Array.Empty<ProviderDiagnosticResponse>(), JsonOptions);
        candidate.SelectedMatchKey = null;
    }

    private static void ApplySelectedMatch(
        LibraryCandidate candidate,
        MetadataMatchOptionResponse selected,
        IReadOnlyCollection<MetadataMatchOptionResponse> alternatives)
    {
        candidate.Title = selected.Title;
        candidate.NormalizedTitle = NormalizeTitle(selected.Title);
        candidate.Description = string.IsNullOrWhiteSpace(selected.Description) ? candidate.Description : selected.Description;
        candidate.Studio = selected.Studio;
        candidate.ReleaseYear = selected.ReleaseYear;
        candidate.CoverImagePath = selected.CoverImagePath;
        candidate.GenresSerialized = string.Join(';', selected.Genres);
        candidate.MetadataProvider = selected.Provider;
        candidate.MetadataSourceUrl = selected.SourceUrl;
        candidate.ConfidenceScore = selected.Score;
        candidate.MatchDecision = "Review";
        candidate.MatchSummary = $"Manual override selected '{selected.Title}' as the metadata match.";
        candidate.WinningSignalsJson = JsonSerializer.Serialize(new[] { $"Manual metadata override selected '{selected.Title}'." }, JsonOptions);
        candidate.WarningSignalsJson = JsonSerializer.Serialize(Array.Empty<string>(), JsonOptions);
        candidate.ProviderDiagnosticsJson = JsonSerializer.Serialize(Array.Empty<ProviderDiagnosticResponse>(), JsonOptions);
        candidate.AlternativeMatchesJson = JsonSerializer.Serialize(alternatives, JsonOptions);
        candidate.SelectedMatchKey = selected.Key;
    }

    private async Task<ManualMetadataSearchResponse> SearchMetadataAsync(
        MutableCandidateGroup localGroup,
        string query,
        string? entrypointHint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new AppValidationException("Query is required.");
        }

        var metadataSearch = await SearchWithFallbackAsync(query.Trim(), entrypointHint, cancellationToken);
        var metadataSettings = await settingsService.GetMetadataRuntimeAsync(cancellationToken);
        var evaluation = EvaluateMetadata(localGroup, metadataSearch, metadataSettings);
        return new ManualMetadataSearchResponse(
            query.Trim(),
            localGroup.Title,
            evaluation.Summary,
            evaluation.WinningSignals,
            evaluation.WarningSignals,
            evaluation.ProviderDiagnostics,
            evaluation.AlternativeMatches);
    }

    private static bool ShouldAutoTidy(LibraryCandidate candidate) =>
        !string.IsNullOrWhiteSpace(candidate.MetadataProvider) &&
        (string.Equals(candidate.MatchDecision, "AutoImport", StringComparison.OrdinalIgnoreCase) ||
         !string.IsNullOrWhiteSpace(candidate.SelectedMatchKey));

    private async Task<(List<LibraryCandidateSourceResponse> Sources, string? Summary)> TryApplyFolderTidyAsync(
        LibraryCandidate candidate,
        List<LibraryCandidateSourceResponse> sources,
        string canonicalTitle,
        CancellationToken cancellationToken)
    {
        var root = candidate.LibraryRoot ?? await dbContext.LibraryRoots.FirstOrDefaultAsync(x => x.Id == candidate.LibraryRootId, cancellationToken);
        if (root is null || string.IsNullOrWhiteSpace(root.Path))
        {
            return (sources, null);
        }

        var containerPath = ResolveTidyContainerPath(root.Path, sources);
        if (string.IsNullOrWhiteSpace(containerPath) || !Directory.Exists(containerPath))
        {
            return (sources, null);
        }

        var currentName = Path.GetFileName(containerPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetName = BuildCanonicalFolderName(canonicalTitle);
        if (string.IsNullOrWhiteSpace(targetName) || string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            return (sources, null);
        }

        var parentPath = Path.GetDirectoryName(containerPath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return (sources, null);
        }

        var targetPath = Path.Combine(parentPath, targetName);
        if (!IsPathUnderRoot(targetPath, root.Path) || Directory.Exists(targetPath) || File.Exists(targetPath))
        {
            return (sources, $"Library tidy skipped: unable to rename '{currentName}' to '{targetName}'.");
        }

        try
        {
            Directory.Move(containerPath, targetPath);
        }
        catch (Exception ex)
        {
            return (sources, $"Library tidy skipped: could not rename '{currentName}' to '{targetName}' ({ex.Message}).");
        }

        var updatedSources = sources
            .Select(source => source with
            {
                Path = ReplacePathPrefix(source.Path, containerPath, targetPath)
            })
            .ToList();

        candidate.PrimaryPath = ReplacePathPrefix(candidate.PrimaryPath, containerPath, targetPath);
        candidate.SourcesJson = JsonSerializer.Serialize(updatedSources, JsonOptions);
        candidate.LocalTitle = DeriveLocalTitleFromPath(candidate.PrimaryPath);
        candidate.LocalNormalizedTitle = NormalizeTitle(candidate.LocalTitle);
        candidate.LocalDescription = BuildLocalDescriptionForPath(candidate.PrimaryPath);
        candidate.UpdatedAtUtc = DateTimeOffset.UtcNow;

        return (updatedSources, $"Library tidy applied: renamed folder '{currentName}' to '{targetName}'.");
    }

    private static ImportInstallPlan BuildInstallPlan(LibraryCandidate candidate, IReadOnlyCollection<LibraryCandidateSourceResponse> sources)
    {
        var primarySource = sources.OrderBy(x => x.DiscNumber ?? int.MaxValue).ThenBy(x => x.Label).First();
        var sourcePath = primarySource.Path;
        var sourceExtension = Path.GetExtension(sourcePath);
        var isDirectory = Directory.Exists(sourcePath);
        var isDiskImage = !isDirectory && (primarySource.MediaType is MediaType.Iso or MediaType.DiskImage || primarySource.SourceKind is PackageSourceKind.MountedVolume or PackageSourceKind.ExtractedWorkspace);

        if (isDirectory)
        {
            var installerCandidate = InspectInstallerDirectory(sourcePath);
            if (installerCandidate is not null)
            {
                var portableLaunchPath = string.Equals(installerCandidate.Strategy, "PortableCopy", StringComparison.OrdinalIgnoreCase) &&
                                         !string.IsNullOrWhiteSpace(primarySource.EntrypointHint)
                    ? Path.Combine(sourcePath, primarySource.EntrypointHint.Replace('/', Path.DirectorySeparatorChar))
                    : null;

                return new ImportInstallPlan(
                    installerCandidate.Strategy,
                    installerCandidate.Tag,
                    installerCandidate.ScriptPath,
                    installerCandidate.InstallerFamily,
                    installerCandidate.InstallerRelativePath,
                    installerCandidate.SilentArguments,
                    portableLaunchPath,
                    BuildDetectionRules(candidate.Title, portableLaunchPath, installerCandidate.Strategy, installerCandidate.InstallerFamily),
                    installerCandidate.Diagnostics);
            }

            if (!string.IsNullOrWhiteSpace(primarySource.EntrypointHint))
            {
                var launchPath = Path.Combine(sourcePath, primarySource.EntrypointHint.Replace('/', Path.DirectorySeparatorChar));
                return new ImportInstallPlan(
                    "PortableDirect",
                    "portable-direct",
                    "builtin:portable-copy",
                    "Portable",
                    primarySource.EntrypointHint.Replace('/', '\\'),
                    null,
                    launchPath,
                    BuildDetectionRules(candidate.Title, launchPath, "PortableCopy", "Portable"),
                    $"Detected portable folder import with entrypoint '{primarySource.EntrypointHint}'.");
            }
        }

        if (string.Equals(sourceExtension, ".msi", StringComparison.OrdinalIgnoreCase))
        {
            return new ImportInstallPlan(
                "AutoInstall",
                "msi-installer",
                "builtin:auto-install",
                "Msi",
                Path.GetFileName(sourcePath),
                "/qn /norestart",
                null,
                BuildDetectionRules(candidate.Title, null, "AutoInstall", "Msi"),
                $"Detected MSI installer '{Path.GetFileName(sourcePath)}'.");
        }

        if (isDiskImage)
        {
            return new ImportInstallPlan(
                "AutoInstall",
                "auto-install",
                "builtin:auto-install",
                "Unknown",
                null,
                null,
                null,
                BuildDetectionRules(candidate.Title, null, "AutoInstall", "Unknown"),
                $"Disk image '{Path.GetFileName(sourcePath)}' — installer family will be detected at mount time.");
        }

        return new ImportInstallPlan(
            "AutoInstall",
            "auto-install",
            "builtin:auto-install",
            "Unknown",
            null,
            null,
            null,
            BuildDetectionRules(candidate.Title, null, "AutoInstall", "Unknown"),
            $"Installer family could not be pre-determined for '{candidate.PrimaryPath}' — will be detected at install time.");
    }

    private static IReadOnlyCollection<CreateDetectionRuleRequest> BuildDetectionRules(string title, string? launchPath, string installStrategy, string installerFamily)
    {
        var rules = new List<CreateDetectionRuleRequest>();
        if (!string.IsNullOrWhiteSpace(launchPath))
        {
            rules.Add(new CreateDetectionRuleRequest("FileExists", launchPath));
        }

        if (string.Equals(installStrategy, "AutoInstall", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(installerFamily, "Msi", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(title))
        {
            rules.Add(new CreateDetectionRuleRequest("UninstallEntryExists", title));
        }

        if (rules.Count == 0)
        {
            rules.Add(new CreateDetectionRuleRequest("FileExists", @"%INSTALL_ROOT%\installed\gamarr-library-import.marker"));
        }

        return rules;
    }

    private static InstallerInspectionResult? InspectInstallerDirectory(string sourcePath)
    {
        var allFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var inspectionCandidates = new List<(string Path, string DiagnosticPrefix, int Priority)>();

        var msi = allFiles.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".msi", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(msi))
        {
            inspectionCandidates.Add((msi, $"Detected MSI installer '{Path.GetFileName(msi)}' in '{sourcePath}'.", 200));
        }

        var autorunCommand = ReadAutorunCommand(sourcePath);
        if (!string.IsNullOrWhiteSpace(autorunCommand))
        {
            var autorunCandidate = ResolveAutorunExecutable(sourcePath, autorunCommand);
            if (!string.IsNullOrWhiteSpace(autorunCandidate))
            {
                inspectionCandidates.Add((autorunCandidate, $"Detected autorun metadata in '{sourcePath}'.", GetInstallerCandidatePriority(sourcePath, autorunCandidate, preferAutorun: true)));
            }
        }

        foreach (var installerExe in allFiles.Where(LooksLikeInstallerExecutable))
        {
            inspectionCandidates.Add((installerExe, $"Detected Windows installer '{Path.GetFileName(installerExe)}' in '{sourcePath}'.", GetInstallerCandidatePriority(sourcePath, installerExe, preferAutorun: false)));
        }

        var inspected = inspectionCandidates
            .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => (Inspection: InspectInstallerFile(sourcePath, candidate.Path, candidate.DiagnosticPrefix), candidate.Priority))
            .OrderByDescending(candidate => candidate.Priority)
            .ThenByDescending(candidate => GetInstallerSafetyScore(candidate.Inspection))
            .ThenBy(candidate => candidate.Inspection.InstallerRelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Inspection)
            .ToArray();

        return inspected.FirstOrDefault();
    }

    private static InstallerInspectionResult InspectInstallerFile(string sourcePath, string installerPath, string diagnosticPrefix)
    {
        var family = DetectInstallerFamily(installerPath);
        var relativePath = Path.GetRelativePath(sourcePath, installerPath).Replace(Path.DirectorySeparatorChar, '\\');
        var silentArgs = family switch
        {
            "Msi" => "/qn /norestart",
            "Inno" => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
            "Nsis" => "/S",
            "InstallShield" => "/s",
            _ => null
        };
        var diagnostics = family is not "Unknown"
            ? $"{diagnosticPrefix} Classified installer family as {family}."
            : $"{diagnosticPrefix} Installer family unknown — will attempt silent install at run time.";

        return new InstallerInspectionResult(
            "AutoInstall",
            "auto-install",
            "builtin:auto-install",
            family,
            relativePath,
            silentArgs,
            diagnostics);
    }

    private static int GetInstallerSafetyScore(InstallerInspectionResult inspection) =>
        inspection.InstallerFamily switch
        {
            "Msi" => 50,
            "Inno" => 40,
            "Nsis" => 30,
            "InstallShield" => 20,
            _ => 10
        };

    private static bool LooksLikeInstallerExecutable(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        if (IsIgnoredInstallerSegment(parent))
        {
            return false;
        }

        return fileName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("install", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("autorun", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetInstallerCandidatePriority(string rootPath, string installerPath, bool preferAutorun)
    {
        var relativePath = Path.GetRelativePath(rootPath, installerPath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(IsIgnoredInstallerSegment))
        {
            return -100;
        }

        var fileName = Path.GetFileName(installerPath);
        var depth = Math.Max(0, segments.Length - 1);
        var score = preferAutorun ? 180 : 100 - (depth * 10);

        if (fileName.Equals("setup.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("install.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("installer.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("autorun.exe", StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }
        else if (fileName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Contains("install", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Contains("autorun", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        return score;
    }

    private static bool IsIgnoredInstallerSegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment) &&
        (segment.Equals("directx", StringComparison.OrdinalIgnoreCase) ||
         segment.Equals("dxmedia", StringComparison.OrdinalIgnoreCase) ||
         segment.Equals("redist", StringComparison.OrdinalIgnoreCase) ||
         segment.Equals("redistributable", StringComparison.OrdinalIgnoreCase) ||
         segment.Equals("redistributables", StringComparison.OrdinalIgnoreCase) ||
         segment.Equals("support", StringComparison.OrdinalIgnoreCase) ||
         segment.Equals("crack", StringComparison.OrdinalIgnoreCase) ||
         segment.Equals("keygen", StringComparison.OrdinalIgnoreCase));

    private static string DetectInstallerFamily(string installerPath)
    {
        if (string.Equals(Path.GetExtension(installerPath), ".msi", StringComparison.OrdinalIgnoreCase))
        {
            return "Msi";
        }

        try
        {
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(installerPath);
            var signature = $"{version.FileDescription} {version.CompanyName} {version.ProductName}";
            if (signature.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase))
            {
                return "Inno";
            }

            if (signature.Contains("Nullsoft", StringComparison.OrdinalIgnoreCase) || signature.Contains("NSIS", StringComparison.OrdinalIgnoreCase))
            {
                return "Nsis";
            }

            if (signature.Contains("InstallShield", StringComparison.OrdinalIgnoreCase))
            {
                return "InstallShield";
            }
        }
        catch
        {
        }

        try
        {
            var probeLength = (int)Math.Min(new FileInfo(installerPath).Length, 1_048_576);
            if (probeLength > 0)
            {
                using var stream = File.OpenRead(installerPath);
                var buffer = new byte[probeLength];
                _ = stream.Read(buffer, 0, probeLength);
                var ascii = System.Text.Encoding.Latin1.GetString(buffer);
                var unicode = System.Text.Encoding.Unicode.GetString(buffer);
                var probe = string.Concat(ascii, " ", unicode);

                if (probe.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase))
                {
                    return "Inno";
                }

                if (probe.Contains("Nullsoft", StringComparison.OrdinalIgnoreCase) ||
                    probe.Contains("NSIS", StringComparison.OrdinalIgnoreCase))
                {
                    return "Nsis";
                }

                if (probe.Contains("InstallShield", StringComparison.OrdinalIgnoreCase))
                {
                    return "InstallShield";
                }
            }
        }
        catch
        {
        }

        return "Unknown";
    }

    private static string? ReadAutorunCommand(string sourcePath)
    {
        var autorunPath = Path.Combine(sourcePath, "autorun.inf");
        if (!File.Exists(autorunPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(autorunPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("open=", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("shellexecute=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(trimmed.IndexOf('=') + 1)..].Trim().Trim('"');
            }
        }

        return null;
    }

    private static string? ResolveAutorunExecutable(string sourcePath, string command)
    {
        var token = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('"');
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var fullPath = Path.Combine(sourcePath, token.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) ? fullPath : null;
    }

    private static bool ShouldProcessPath(string path, string rootPath, MediaManagementSettingsRuntime mediaSettings)
    {
        var normalizedPath = Path.GetFullPath(path).Replace('/', '\\');
        var normalizedRoot = Path.GetFullPath(rootPath).Replace('/', '\\');
        var relative = normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[normalizedRoot.Length..].TrimStart('\\')
            : normalizedPath;

        if (mediaSettings.IncludePatterns.Count > 0 &&
            !mediaSettings.IncludePatterns.Any(pattern => MatchesWildcard(relative, pattern) || MatchesWildcard(normalizedPath, pattern)))
        {
            return false;
        }

        if (mediaSettings.ExcludePatterns.Any(pattern => MatchesWildcard(relative, pattern) || MatchesWildcard(normalizedPath, pattern)))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesWildcard(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var regex = "^" + Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private async Task<DiscoveryResult> DiscoverCandidatesAsync(
        LibraryRoot root,
        LibraryScan scan,
        MetadataSettingsRuntime metadataSettings,
        MediaManagementSettingsRuntime mediaSettings,
        CancellationToken cancellationToken,
        Func<int, int, int, int, Task>? onProgress = null)
    {
        var groups = new Dictionary<string, MutableCandidateGroup>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root.Path, 0));

        var directoriesScanned = 0;
        var filesScanned = 0;
        var folderCandidates = 0;
        var imageCandidates = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            if (!ShouldProcessPath(current.Path, root.Path, mediaSettings))
            {
                continue;
            }

            directoriesScanned++;

            string[] directories;
            string[] files;
            try
            {
                directories = Directory.GetDirectories(current.Path);
                files = Directory.GetFiles(current.Path);
            }
            catch
            {
                scan.ErrorsCount++;
                continue;
            }

            filesScanned += files.Length;

            var executables = files.Where(IsGameExecutable).ToArray();
            var rootFileNames = files.Select(Path.GetFileName).OfType<string>().ToArray();
            if (current.Depth > 0 && executables.Length > 0 && ShouldCreateFolderCandidate(root.Path, current.Path))
            {
                if (ShouldPreferFolderAsPrimaryCandidate(current.Path, executables, rootFileNames))
                {
                    folderCandidates++;
                    var hint = TryReadHintFile(current.Path);
                    var title = !string.IsNullOrWhiteSpace(hint?.Name)
                        ? CleanTitle(hint.Name)
                        : CleanTitle(Path.GetFileName(current.Path));
                    var normalizedTitle = NormalizeTitle(title);
                    var entrypoint = SelectEntrypoint(current.Path, executables);
                    var groupKey = $"folder::{current.Path}";
                    groups[groupKey] = new MutableCandidateGroup(
                        title,
                        normalizedTitle,
                        current.Path,
                        entrypoint is null ? string.Empty : $"Folder candidate detected at {current.Path}.",
                        [
                            new LibraryCandidateSourceResponse(
                                "Installed Folder",
                                current.Path,
                                MediaType.InstallerFolder,
                                PackageSourceKind.DirectFolder,
                                ScratchPolicy.Temporary,
                                1,
                                entrypoint,
                                hint is not null)
                        ]);
                }
            }

            foreach (var file in files.Where(IsDiskImageFile).Where(file => ShouldProcessPath(file, root.Path, mediaSettings)))
            {
                if (IsCompanionImageDataFileWithDescriptor(file))
                {
                    continue;
                }

                imageCandidates++;
                var fileName = Path.GetFileNameWithoutExtension(file);
                var title = CleanTitle(fileName);
                var normalizedTitle = NormalizeTitle(title);
                var discNumber = ParseDiscNumber(fileName);
                var extension = Path.GetExtension(file);
                var sourceKind = InferSourceKind(extension);
                var mediaType = string.Equals(extension, ".iso", StringComparison.OrdinalIgnoreCase) ? MediaType.Iso : MediaType.DiskImage;
                var containerDirectory = FindPreferredContainerDirectory(root.Path, Path.GetDirectoryName(file) ?? root.Path);
                var hint = TryReadHintFile(containerDirectory);
                var containerName = Path.GetFileName(containerDirectory);
                if (!string.IsNullOrWhiteSpace(hint?.Name))
                {
                    title = CleanTitle(hint.Name);
                    normalizedTitle = NormalizeTitle(title);
                }
                else if (!string.IsNullOrWhiteSpace(containerName))
                {
                    title = CleanTitle(containerName);
                    normalizedTitle = NormalizeTitle(title);
                }

                var groupKey = $"image::{containerDirectory}::{normalizedTitle}";

                if (!groups.TryGetValue(groupKey, out var group))
                {
                    group = new MutableCandidateGroup(title, normalizedTitle, file, $"Disc image set discovered under {containerDirectory}.", []);
                    groups.Add(groupKey, group);
                }

                group.Sources.Add(new LibraryCandidateSourceResponse(
                    discNumber.HasValue ? $"Disc {discNumber.Value}" : Path.GetFileName(file),
                    file,
                    mediaType,
                    sourceKind,
                    ScratchPolicy.Temporary,
                    discNumber,
                    null,
                    hint is not null));
            }

            if (current.Depth >= MaxScanDepth)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (IgnoredDirectoryNames.Contains(Path.GetFileName(directory)) ||
                    !ShouldProcessPath(directory, root.Path, mediaSettings))
                {
                    continue;
                }

                pending.Push((directory, current.Depth + 1));
            }
        }

        var candidates = new List<DiscoveredCandidate>();
        var groupList = groups.Values.ToList();
        var total = groupList.Count;
        if (onProgress is not null)
        {
            await onProgress(directoriesScanned, filesScanned, 0, total);
        }

        for (var i = 0; i < groupList.Count; i++)
        {
            var group = groupList[i];
            var groupEntrypoint = group.Sources.FirstOrDefault()?.EntrypointHint;
            var metadataSearch = await SearchWithFallbackAsync(group.Title, groupEntrypoint, cancellationToken);
            candidates.Add(ToDiscoveredCandidate(group, metadataSearch, metadataSettings));
            if (onProgress is not null)
            {
                await onProgress(directoriesScanned, filesScanned, i + 1, total);
            }
        }

        var contentKind = imageCandidates == 0 && folderCandidates > 0
            ? LibraryRootContentKind.InstalledLibrary
            : folderCandidates == 0 && imageCandidates > 0
                ? LibraryRootContentKind.MediaArchive
                : folderCandidates > 0 || imageCandidates > 0
                    ? LibraryRootContentKind.Mixed
                    : LibraryRootContentKind.Unknown;

        return new DiscoveryResult(directoriesScanned, filesScanned, contentKind, candidates);
    }

    private static DiscoveredCandidate ToDiscoveredCandidate(
        MutableCandidateGroup group,
        MetadataSearchResult metadataSearch,
        MetadataSettingsRuntime metadataSettings)
    {
        var evaluation = EvaluateMetadata(group, metadataSearch, metadataSettings);
        var title = evaluation.SelectedMatch?.Title ?? group.Title;
        var normalizedTitle = NormalizeTitle(title);
        return new DiscoveredCandidate(
            group.Title,
            group.NormalizedTitle,
            group.Description,
            title,
            normalizedTitle,
            evaluation.SelectedMatch?.Description ?? group.Description,
            evaluation.SelectedMatch?.Studio ?? string.Empty,
            evaluation.SelectedMatch?.ReleaseYear,
            evaluation.SelectedMatch?.CoverImagePath,
            evaluation.SelectedMatch?.Genres ?? Array.Empty<string>(),
            evaluation.SelectedMatch?.Provider,
            evaluation.SelectedMatch?.SourceUrl,
            evaluation.SelectedScore,
            evaluation.Decision,
            evaluation.Summary,
            evaluation.WinningSignals,
            evaluation.WarningSignals,
            evaluation.ProviderDiagnostics,
            evaluation.AlternativeMatches,
            evaluation.SelectedMatch?.Key,
            group.PrimaryPath,
            group.Sources.ToArray());
    }

    private static MetadataEvaluation EvaluateMetadata(
        MutableCandidateGroup group,
        MetadataSearchResult metadataSearch,
        MetadataSettingsRuntime metadataSettings)
    {
        var winningSignals = new List<string>();
        var warningSignals = new List<string>();
        var providerDiagnostics = new List<ProviderDiagnosticResponse>();
        var alternatives = new List<MetadataMatchOptionResponse>();
        var ranked = metadataSearch.Matches
            .Select(match => EvaluateMetadataCandidate(group, match))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Match.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ranked.Length == 0)
        {
            warningSignals.Add("No provider metadata match was found.");
            providerDiagnostics.AddRange(metadataSearch.Diagnostics.Select(d => new ProviderDiagnosticResponse(
                d.Provider,
                d.Status,
                d.CandidateCount,
                null,
                false,
                d.Summary,
                d.TopTitles)));
            return new MetadataEvaluation(
                null,
                0.55d,
                "LocalOnly",
                "No remote metadata match was found. Keep this candidate local-only or review manually.",
                Array.Empty<string>(),
                warningSignals.ToArray(),
                providerDiagnostics.ToArray(),
                Array.Empty<MetadataMatchOptionResponse>());
        }

        var perProviderBest = ranked
            .GroupBy(x => x.Match.Provider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.First(), StringComparer.OrdinalIgnoreCase);

        MetadataScore best = ranked[0];
        if (metadataSettings.PreferIgdb &&
            perProviderBest.TryGetValue("IGDB", out var igdbBest) &&
            perProviderBest.TryGetValue("Steam", out var steamBest) &&
            igdbBest.Score >= steamBest.Score - 0.03d)
        {
            best = igdbBest;
        }

        var runnerUp = ranked
            .Where(x => !string.Equals(x.Match.Provider, best.Match.Provider, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(x.Match.Key, best.Match.Key, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        alternatives.AddRange(ranked.Select(x =>
            new MetadataMatchOptionResponse(
                x.Match.Key,
                x.Match.Provider,
                x.Match.Title,
                x.Match.Description,
                x.Match.ReleaseYear,
                x.Match.Studio,
                x.Match.CoverImagePath,
                x.Match.BackdropImagePath,
                x.Match.ScreenshotImageUrls,
                x.Match.SourceUrl,
                x.Match.Genres,
                x.Match.Themes,
                x.Match.Platforms,
                x.Score,
                x.ReasonSummary)));

        if (best.ExactNormalizedMatch)
        {
            winningSignals.Add("Exact normalized title match.");
        }
        else if (best.TokenOverlap >= 0.8d)
        {
            winningSignals.Add($"Strong token overlap ({best.TokenOverlap:P0}).");
        }

        if (best.YearAgreement)
        {
            winningSignals.Add("Release year agrees with local evidence.");
        }

        if (best.SourceShapeAgreement)
        {
            winningSignals.Add("Metadata title agrees with the discovered source shape.");
        }

        warningSignals.AddRange(best.Warnings);

        if (runnerUp is not null && Math.Abs(best.Score - runnerUp.Score) < 0.08d)
        {
            warningSignals.Add($"Alternative match '{runnerUp.Match.Title}' scored close to the current winner.");
        }

        foreach (var diagnostic in metadataSearch.Diagnostics)
        {
            perProviderBest.TryGetValue(diagnostic.Provider, out var providerBest);
            var isWinner = providerBest is not null &&
                           string.Equals(providerBest.Match.Provider, best.Match.Provider, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(providerBest.Match.Key, best.Match.Key, StringComparison.OrdinalIgnoreCase);

            var providerSummary = diagnostic.Status switch
            {
                "NoResults" => $"{diagnostic.Provider} returned no candidates.",
                "Error" => diagnostic.Summary,
                _ when providerBest is null => $"{diagnostic.Provider} returned no scoreable metadata results.",
                _ when isWinner => $"{diagnostic.Provider} won with '{providerBest.Match.Title}' ({providerBest.Score:P0}).",
                _ => $"{diagnostic.Provider} was outranked by {best.Match.Provider}."
            };

            providerDiagnostics.Add(new ProviderDiagnosticResponse(
                diagnostic.Provider,
                diagnostic.Status,
                diagnostic.CandidateCount,
                providerBest?.Score,
                isWinner,
                providerSummary,
                diagnostic.TopTitles));
        }

        var nearPerfect = best.Score >= metadataSettings.AutoImportThreshold &&
                          (runnerUp is null || best.Score - runnerUp.Score >= 0.1d) &&
                          !best.HasEditionMismatch &&
                          (best.TokenOverlap >= 0.9d || best.AllTokensFuzzyMatch);

        var reviewable = best.Score >= metadataSettings.ReviewThreshold && !best.HasFamilyMismatch;
        var decision = nearPerfect ? "AutoImport" : reviewable ? "Review" : "LocalOnly";
        var summary = nearPerfect
            ? $"Auto-import ready: '{best.Match.Title}' is a near-perfect metadata match."
            : reviewable
                ? $"Review required: '{best.Match.Title}' is the best metadata match, but it is not safe enough to auto-import."
                : "Local-only recommended: metadata candidates were too weak or too risky to trust.";

        return new MetadataEvaluation(
            best.Match,
            best.Score,
            decision,
            summary,
            winningSignals.ToArray(),
            warningSignals.ToArray(),
            providerDiagnostics.ToArray(),
            alternatives.ToArray());
    }

    private static MetadataScore EvaluateMetadataCandidate(MutableCandidateGroup group, LibraryMetadataMatch match)
    {
        var localTokens = TokenizeForMatching(group.Title, removeEditionTokens: false);
        var remoteTokens = TokenizeForMatching(match.Title, removeEditionTokens: false);
        var localCoreTokens = TokenizeForMatching(group.Title, removeEditionTokens: true);
        var remoteCoreTokens = TokenizeForMatching(match.Title, removeEditionTokens: true);
        var localNormalized = string.Join('-', localCoreTokens);
        var remoteNormalized = string.Join('-', remoteCoreTokens);

        var score = 0.45d;
        var warnings = new List<string>();
        var exactNormalizedMatch = !string.IsNullOrWhiteSpace(localNormalized) && string.Equals(localNormalized, remoteNormalized, StringComparison.Ordinal);
        if (exactNormalizedMatch)
        {
            score += 0.35d;
        }

        var tokenOverlap = ComputeTokenOverlap(localCoreTokens, remoteCoreTokens);
        score += Math.Min(0.3d, tokenOverlap * 0.3d);

        // If all tokens matched (even fuzzily) and counts are equal, treat as a near-exact match.
        // This handles spelling variants (honour/honor, colour/color) and minor typos (assult/assault)
        // without giving the full +0.35 exact bonus.
        var allTokensFuzzyMatch = !exactNormalizedMatch &&
            localCoreTokens.Length > 0 &&
            localCoreTokens.Length == remoteCoreTokens.Length &&
            tokenOverlap >= 1.0d;
        if (allTokensFuzzyMatch)
        {
            score += 0.25d;
        }

        var localEditionTokens = ExtractEditionTokens(group.Title);
        var remoteEditionTokens = ExtractEditionTokens(match.Title);
        var hasEditionMismatch = localEditionTokens.Count != 0 || remoteEditionTokens.Count != 0
            ? !localEditionTokens.SetEquals(remoteEditionTokens)
            : false;
        if (hasEditionMismatch)
        {
            score -= 0.22d;
            warnings.Add("Edition keywords differ between the local title and metadata.");
        }

        var hasFamilyMismatch = localCoreTokens.Length > 0 && remoteCoreTokens.Length > 0 && tokenOverlap < 0.45d;
        if (hasFamilyMismatch)
        {
            score -= 0.18d;
            warnings.Add("Core title tokens do not align strongly enough.");
        }

        var sourceShapeAgreement = !group.Sources.Any(x => x.MediaType is MediaType.Iso or MediaType.DiskImage) ||
                                   !remoteEditionTokens.Contains("demo");
        if (!sourceShapeAgreement)
        {
            score -= 0.08d;
            warnings.Add("Metadata implies a demo-style edition that does not fit the discovered source.");
        }

        var yearAgreement = false;
        var localYear = ExtractYearHint(group.PrimaryPath);
        if (localYear.HasValue && match.ReleaseYear.HasValue)
        {
            if (Math.Abs(localYear.Value - match.ReleaseYear.Value) <= 1)
            {
                score += 0.05d;
                yearAgreement = true;
            }
            else
            {
                score -= 0.08d;
                warnings.Add("Release year inferred from local evidence disagrees with metadata.");
            }
        }

        score = Math.Clamp(Math.Round(score, 2), 0.05d, 0.99d);
        var reasonSummary = exactNormalizedMatch
            ? "Exact normalized title match."
            : allTokensFuzzyMatch
                ? "Near-exact match via spelling variant or minor typo correction."
                : tokenOverlap >= 0.8d
                    ? $"Strong token overlap ({tokenOverlap:P0})."
                    : warnings.FirstOrDefault() ?? "Partial title match.";

        return new MetadataScore(
            match,
            score,
            tokenOverlap,
            exactNormalizedMatch,
            hasEditionMismatch,
            hasFamilyMismatch,
            yearAgreement,
            sourceShapeAgreement,
            warnings.ToArray(),
            reasonSummary,
            allTokensFuzzyMatch);
    }

    private static bool IsGameExecutable(string path)
    {
        if (!ExecutableExtensions.Contains(Path.GetExtension(path)))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName is not null &&
               !fileName.Contains("setup", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Contains("install", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Contains("unins", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Contains("redist", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Contains("dxsetup", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiskImageFile(string path) => DiskImageExtensions.Contains(Path.GetExtension(path));

    private static bool IsCompanionImageDataFileWithDescriptor(string path)
    {
        var extension = Path.GetExtension(path);
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(path);

        return extension.ToLowerInvariant() switch
        {
            ".bin" => File.Exists(Path.Combine(directory, $"{baseName}.cue")),
            ".mdf" => File.Exists(Path.Combine(directory, $"{baseName}.mds")),
            ".img" => File.Exists(Path.Combine(directory, $"{baseName}.ccd")),
            _ => false
        };
    }

    private static string? SelectEntrypoint(string directoryPath, IReadOnlyCollection<string> executables)
    {
        var best = executables
            .OrderBy(path => Path.GetFileName(path).Contains("launcher", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => Path.GetFileName(path))
            .FirstOrDefault();

        return best is null ? null : Path.GetRelativePath(directoryPath, best).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static PackageSourceKind InferSourceKind(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".iso" or ".img" or ".vhd" or ".vhdx" => PackageSourceKind.MountedVolume,
            _ => PackageSourceKind.ExtractedWorkspace
        };

    private static int? ParseDiscNumber(string value)
    {
        var match = DiscRegex().Match(value);
        return match.Success && int.TryParse(match.Groups["disc"].Value, out var discNumber) ? discNumber : null;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static string PrepareSearchQuery(string title)
    {
        // Preserve year from parentheses before stripping them — helps disambiguate (e.g. "Doom 1993")
        var yearMatch = Regex.Match(title, @"\(((?:19|20)\d{2})\)");
        var yearSuffix = yearMatch.Success ? " " + yearMatch.Groups[1].Value : "";
        // Strip parenthetical and bracketed annotations: (USA), (Europe), [No-Intro], [!], etc.
        var stripped = Regex.Replace(title, @"\s*\([^)]*\)", " ");
        stripped = Regex.Replace(stripped, @"\s*\[[^\]]*\]", " ");
        // Strip loose version tags not in brackets: v1.2, v2.0
        stripped = Regex.Replace(stripped, @"\bv\d+(?:\.\d+)*\b", " ", RegexOptions.IgnoreCase);
        stripped = CollapseSplitAlphaNumericTokens(stripped);
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(stripped) ? title : (stripped + yearSuffix).Trim();
    }

    private async Task<MetadataSearchResult> SearchWithFallbackAsync(
        string title,
        string? entrypointHint,
        CancellationToken cancellationToken)
    {
        var queries = BuildMetadataSearchQueries(title, entrypointHint);
        var seenMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedMatches = new List<LibraryMetadataMatch>();
        var diagnosticsByProvider = new Dictionary<string, ProviderDiagnosticAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var query in queries)
        {
            var result = await metadataProvider.SearchMatchesAsync(query, cancellationToken);
            foreach (var match in result.Matches)
            {
                if (seenMatches.Add($"{match.Provider}:{match.Key}"))
                {
                    mergedMatches.Add(match);
                }
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                if (!diagnosticsByProvider.TryGetValue(diagnostic.Provider, out var accumulator))
                {
                    accumulator = new ProviderDiagnosticAccumulator();
                    diagnosticsByProvider[diagnostic.Provider] = accumulator;
                }

                accumulator.Record(diagnostic);
            }

            if (mergedMatches.Count >= 8)
            {
                break;
            }
        }

        var mergedDiagnostics = diagnosticsByProvider
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => kvp.Value.ToResponse(kvp.Key))
            .ToArray();

        return new MetadataSearchResult(mergedMatches, mergedDiagnostics);
    }

    private static IReadOnlyList<string> BuildMetadataSearchQueries(string title, string? entrypointHint)
    {
        var queries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddQuery(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var prepared = PrepareSearchQuery(candidate);
            if (prepared.Length < 3)
            {
                return;
            }

            if (seen.Add(prepared))
            {
                queries.Add(prepared);
            }
        }

        var primary = PrepareSearchQuery(title);
        AddQuery(primary);
        AddQuery(CollapseSplitAlphaNumericTokens(primary));

        foreach (var separator in SubtitleSeparators)
        {
            var separatorIndex = primary.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (separatorIndex > 0)
            {
                AddQuery(primary[..separatorIndex].Trim());
            }
        }

        var editionTrimmed = TrimTrailingEditionWords(primary);
        AddQuery(editionTrimmed);

        var coreTokens = TokenizeForMatching(primary, removeEditionTokens: true);
        var tokenTrimmed = string.Join(' ', coreTokens);
        AddQuery(tokenTrimmed);

        for (var length = coreTokens.Length - 1; length >= 2; length--)
        {
            AddQuery(string.Join(' ', coreTokens.Take(length)));
        }

        foreach (var collapsedVariant in BuildCollapsedTokenQueries(primary))
        {
            AddQuery(collapsedVariant);
        }

        if (!string.IsNullOrWhiteSpace(entrypointHint))
        {
            var exeName = Path.GetFileNameWithoutExtension(entrypointHint.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(exeName))
            {
                AddQuery(CleanTitle(exeName));
            }
        }

        return queries;
    }

    private static string TrimTrailingEditionWords(string value)
    {
        var tokens = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        while (tokens.Count > 0 && EditionSensitiveTokens.Contains(tokens[^1], StringComparer.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        return tokens.Count == 0 ? value : string.Join(' ', tokens);
    }

    private static string? TryReadNfoTitle(string directoryPath)
    {
        try
        {
            var patterns = new[] { "*.nfo", "readme.txt", "info.txt" };
            foreach (var pattern in patterns)
            {
                var file = Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (file is null) continue;
                var firstLine = File.ReadLines(file).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                if (!string.IsNullOrWhiteSpace(firstLine))
                    return firstLine.Trim()[..Math.Min(120, firstLine.Trim().Length)];
            }
        }
        catch { /* ignore I/O errors */ }
        return null;
    }

    private static GamarrHint? TryReadHintFile(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return null;
        }

        var hintPath = Path.Combine(directoryPath, "gamarr.json");
        if (!File.Exists(hintPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GamarrHint>(
                File.ReadAllText(hintPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static string CleanTitle(string value)
    {
        var withoutDisc = DiscRegex().Replace(value, " ");
        // CamelCase: "GrandTheftAutoSanAndreas" → "Grand Theft Auto San Andreas"
        var withCamel = Regex.Replace(withoutDisc, @"(?<=[a-z])(?=[A-Z])", " ");
        // Digit-letter boundaries: "Doom3" → "Doom 3"
        var withBoundaries = Regex.Replace(withCamel, @"([a-zA-Z])(\d)", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"(\d)([a-zA-Z])", "$1 $2");
        var normalizedWhitespace = Regex.Replace(withBoundaries.Replace('_', ' ').Replace('.', ' '), @"\s+", " ").Trim();
        normalizedWhitespace = Regex.Replace(normalizedWhitespace, @"\b(\d+)\s+d\b", "$1D", RegexOptions.IgnoreCase);
        return string.IsNullOrWhiteSpace(normalizedWhitespace) ? value : normalizedWhitespace;
    }

    private static string NormalizeTitle(string value)
    {
        var clean = string.Join('-', TokenizeForMatching(value, removeEditionTokens: true));
        return string.IsNullOrWhiteSpace(clean) ? "untitled" : clean;
    }

    private static string[] TokenizeForMatching(string value, bool removeEditionTokens)
    {
        var collapsed = CollapseSplitAlphaNumericTokens(value).ToLowerInvariant();
        var rawTokens = Regex.Split(collapsed, @"[^a-z0-9]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .SelectMany(ExpandSearchToken)
            .Where(token => token.Length > 1 || token.All(char.IsDigit))
            .Where(token => !NoiseTitleTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var filtered = removeEditionTokens
            ? rawTokens.Where(token => !EditionSensitiveTokens.Contains(token, StringComparer.OrdinalIgnoreCase)).ToArray()
            : rawTokens;

        // Normalise Roman numerals to Arabic digits: III → 3, IV → 4, etc.
        return filtered.Select(t => RomanNumerals.TryGetValue(t, out var arabic) ? arabic : t).ToArray();
    }

    private static IEnumerable<string> BuildCollapsedTokenQueries(string value)
    {
        var collapsed = CollapseSplitAlphaNumericTokens(value);
        if (!string.Equals(collapsed, value, StringComparison.OrdinalIgnoreCase))
        {
            yield return collapsed;
        }

        var splitTokens = Regex.Split(value, @"[^a-zA-Z0-9]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        for (var index = 0; index < splitTokens.Length - 1; index++)
        {
            var left = splitTokens[index];
            var right = splitTokens[index + 1];
            if (!LooksLikeSplitAlphaNumericPair(left, right))
            {
                continue;
            }

            var rebuilt = new List<string>(splitTokens.Length - 1);
            for (var tokenIndex = 0; tokenIndex < splitTokens.Length; tokenIndex++)
            {
                if (tokenIndex == index)
                {
                    rebuilt.Add(left + right);
                    tokenIndex++;
                    continue;
                }

                rebuilt.Add(splitTokens[tokenIndex]);
            }

            yield return string.Join(' ', rebuilt);
        }
    }

    private static IEnumerable<string> ExpandSearchToken(string token)
    {
        yield return token;

        if (token.Length <= 2 || !token.Any(char.IsLetter) || !token.Any(char.IsDigit))
        {
            yield break;
        }

        var components = Regex.Matches(token, @"[a-z]+|\d+")
            .Select(match => match.Value)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (components.Length <= 1)
        {
            yield break;
        }

        foreach (var component in components)
        {
            yield return component;
        }
    }

    private static string CollapseSplitAlphaNumericTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var collapsed = value;
        var previous = string.Empty;
        while (!string.Equals(previous, collapsed, StringComparison.Ordinal))
        {
            previous = collapsed;
            collapsed = Regex.Replace(
                collapsed,
                @"\b([a-zA-Z])\s+(\d{1,4})(?:\s+([a-zA-Z]))?\b",
                static match =>
                {
                    var left = match.Groups[1].Value;
                    var digits = match.Groups[2].Value;
                    var suffix = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;
                    return left + digits + suffix;
                },
                RegexOptions.IgnoreCase);

            collapsed = Regex.Replace(
                collapsed,
                @"\b(\d{1,4})\s+([a-zA-Z])\b",
                "$1$2",
                RegexOptions.IgnoreCase);
        }

        return collapsed;
    }

    private static bool LooksLikeSplitAlphaNumericPair(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (left.Length == 1 && left.All(char.IsLetter) && right.All(char.IsDigit))
        {
            return true;
        }

        if (left.All(char.IsDigit) && right.Length == 1 && right.All(char.IsLetter))
        {
            return true;
        }

        return false;
    }

    private static HashSet<string> ExtractEditionTokens(string value) =>
        TokenizeForMatching(value, removeEditionTokens: false)
            .Where(token => EditionSensitiveTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static double ComputeTokenOverlap(IReadOnlyCollection<string> localTokens, IReadOnlyCollection<string> remoteTokens)
    {
        if (localTokens.Count == 0 || remoteTokens.Count == 0)
        {
            return 0d;
        }

        var remotePool = remoteTokens.ToList();
        var overlap = 0;
        foreach (var local in localTokens)
        {
            var matchIdx = remotePool.FindIndex(remote => TokensAreFuzzyMatch(local, remote));
            if (matchIdx >= 0)
            {
                overlap++;
                remotePool.RemoveAt(matchIdx);
            }
        }

        var tokenTotal = Math.Max(localTokens.Count, remoteTokens.Count);
        return tokenTotal == 0 ? 0d : Math.Round((double)overlap / tokenTotal, 2);
    }

    private static bool TokensAreFuzzyMatch(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;
        // Only apply fuzzy matching to tokens long enough that a 1-char edit is meaningful.
        // Short tokens (numbers, prepositions, abbreviations) must match exactly so "3" != "4".
        if (left.Length < 5 || right.Length < 5) return false;
        if (Math.Abs(left.Length - right.Length) > 2) return false;
        var maxEdits = left.Length >= 8 ? 2 : 1;
        return ComputeLevenshtein(left, right) <= maxEdits;
    }

    private static int ComputeLevenshtein(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        var prev = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var curr = new int[right.Length + 1];
            curr[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                curr[j] = Math.Min(
                    Math.Min(prev[j] + 1, curr[j - 1] + 1),
                    prev[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            }
            prev = curr;
        }
        return prev[right.Length];
    }

    private sealed class ProviderDiagnosticAccumulator
    {
        private string _status = "NoResults";
        private string _summary = "Provider returned no results.";
        private int _candidateCount;
        private readonly List<string> _topTitles = [];

        public void Record(MetadataProviderSearchDiagnostic diagnostic)
        {
            _candidateCount = Math.Max(_candidateCount, diagnostic.CandidateCount);

            foreach (var title in diagnostic.TopTitles)
            {
                if (!string.IsNullOrWhiteSpace(title) &&
                    !_topTitles.Contains(title, StringComparer.OrdinalIgnoreCase) &&
                    _topTitles.Count < 5)
                {
                    _topTitles.Add(title);
                }
            }

            if (string.Equals(diagnostic.Status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                _status = "Success";
                _summary = diagnostic.Summary;
                return;
            }

            if (string.Equals(diagnostic.Status, "NoResults", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(_status, "Success", StringComparison.OrdinalIgnoreCase))
                {
                    _status = "NoResults";
                    _summary = diagnostic.Summary;
                }
                return;
            }

            if (!string.Equals(_status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                _status = diagnostic.Status;
                _summary = diagnostic.Summary;
            }
        }

        public MetadataProviderSearchDiagnostic ToResponse(string provider) =>
            new(
                provider,
                _status,
                _candidateCount,
                _summary,
                _topTitles.ToArray());
    }

    private static int? ExtractYearHint(string value)
    {
        var match = Regex.Match(value, @"(?<!\d)(19\d{2}|20\d{2})(?!\d)");
        return match.Success && int.TryParse(match.Value, out var year) ? year : null;
    }

    private static bool ShouldCreateFolderCandidate(string rootPath, string currentPath)
    {
        var name = Path.GetFileName(currentPath);
        if (IsJunkDirectory(name))
        {
            return false;
        }

        var parent = Directory.GetParent(currentPath);
        while (parent is not null && !PathsEqual(parent.FullName, rootPath))
        {
            if (LooksLikePrimaryGameRoot(parent.FullName))
            {
                return false;
            }

            parent = parent.Parent;
        }

        return true;
    }

    private static bool ShouldPreferFolderAsPrimaryCandidate(string currentPath, IReadOnlyCollection<string> executables, IReadOnlyCollection<string> rootFiles)
    {
        if (IsJunkDirectory(Path.GetFileName(currentPath)))
        {
            return false;
        }

        var hasInstallerEvidence = rootFiles.Any(name =>
            name.Equals("autorun.inf", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("install", StringComparison.OrdinalIgnoreCase));

        if (hasInstallerEvidence)
        {
            return true;
        }

        return executables.Any(path =>
        {
            var fileName = Path.GetFileName(path);
            return !fileName.Contains("config", StringComparison.OrdinalIgnoreCase) &&
                   !fileName.Contains("editor", StringComparison.OrdinalIgnoreCase) &&
                   !fileName.Contains("unins", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool LooksLikePrimaryGameRoot(string path)
    {
        try
        {
            var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OfType<string>().ToArray();
            // A .bin file is only considered a disk image if a matching .cue file exists
            var hasCue = files.Any(name => name.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
            return files.Any(name =>
                name.Equals("autorun.inf", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) ||
                (hasCue && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) ||
                name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                 (name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                  name.Contains("install", StringComparison.OrdinalIgnoreCase) ||
                  name.Contains("autorun", StringComparison.OrdinalIgnoreCase))));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsJunkDirectory(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = name.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        return JunkDirectoryTokens.Any(token =>
            normalized.Equals(token, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(token + " ", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(" " + token + " ", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(" " + token, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindPreferredContainerDirectory(string rootPath, string candidateDirectory)
    {
        var current = candidateDirectory;
        while (!PathsEqual(current, rootPath))
        {
            if (!IsJunkDirectory(Path.GetFileName(current)))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        return candidateDirectory;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?:disc|cd|dvd)\s*(?<disc>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DiscRegex();

    private sealed record MutableCandidateGroup(
        string Title,
        string NormalizedTitle,
        string PrimaryPath,
        string Description,
        List<LibraryCandidateSourceResponse> Sources);

    private sealed record DiscoveryResult(
        int DirectoriesScanned,
        int FilesScanned,
        LibraryRootContentKind ContentKind,
        IReadOnlyCollection<DiscoveredCandidate> Candidates);

    private sealed record DiscoveredCandidate(
        string LocalTitle,
        string LocalNormalizedTitle,
        string LocalDescription,
        string Title,
        string NormalizedTitle,
        string Description,
        string Studio,
        int? ReleaseYear,
        string? CoverImagePath,
        IReadOnlyCollection<string> Genres,
        string? MetadataProvider,
        string? MetadataSourceUrl,
        double MatchConfidence,
        string MatchDecision,
        string MatchSummary,
        IReadOnlyCollection<string> WinningSignals,
        IReadOnlyCollection<string> WarningSignals,
        IReadOnlyCollection<ProviderDiagnosticResponse> ProviderDiagnostics,
        IReadOnlyCollection<MetadataMatchOptionResponse> AlternativeMatches,
        string? SelectedMatchKey,
        string PrimaryPath,
        IReadOnlyCollection<LibraryCandidateSourceResponse> Sources);

    private sealed record MetadataEvaluation(
        LibraryMetadataMatch? SelectedMatch,
        double SelectedScore,
        string Decision,
        string Summary,
        IReadOnlyCollection<string> WinningSignals,
        IReadOnlyCollection<string> WarningSignals,
        IReadOnlyCollection<ProviderDiagnosticResponse> ProviderDiagnostics,
        IReadOnlyCollection<MetadataMatchOptionResponse> AlternativeMatches);

    private sealed record MetadataScore(
        LibraryMetadataMatch Match,
        double Score,
        double TokenOverlap,
        bool ExactNormalizedMatch,
        bool HasEditionMismatch,
        bool HasFamilyMismatch,
        bool YearAgreement,
        bool SourceShapeAgreement,
        IReadOnlyCollection<string> Warnings,
        string ReasonSummary,
        bool AllTokensFuzzyMatch = false);

    private sealed record ImportInstallPlan(
        string Strategy,
        string Tag,
        string InstallScriptPath,
        string InstallerFamily,
        string? InstallerPath,
        string? SilentArguments,
        string? LaunchExecutablePath,
        IReadOnlyCollection<CreateDetectionRuleRequest> DetectionRules,
        string Diagnostics);

    private sealed record InstallerInspectionResult(
        string Strategy,
        string Tag,
        string ScriptPath,
        string InstallerFamily,
        string InstallerRelativePath,
        string? SilentArguments,
        string Diagnostics);
}
