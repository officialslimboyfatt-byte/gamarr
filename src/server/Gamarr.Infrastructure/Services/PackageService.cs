using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Gamarr.Application.Services;
using Gamarr.Application.Exceptions;
using Gamarr.Domain.Entities;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamarr.Infrastructure.Services;

public sealed class PackageService(
    GamarrDbContext dbContext,
    INormalizationService normalizationService) : IPackageService
{
    public async Task<PackageResponse> CreateAsync(CreatePackageRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidatePackageRequest(request);

        var slug = request.Slug.Trim();
        var existing = await dbContext.Packages.AnyAsync(p => p.Slug == slug, cancellationToken);
        if (existing)
        {
            throw new AppConflictException($"A package with slug '{slug}' already exists.");
        }

        var manifest = PackageManifestBuilder.Build(request);

        var package = new Package
        {
            Slug = slug,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Notes = request.Notes.Trim(),
            TagsSerialized = string.Join(';', request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())),
            GenresSerialized = string.Join(';', request.Genres.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())),
            Studio = request.Studio.Trim(),
            ReleaseYear = request.ReleaseYear,
            CoverImagePath = string.IsNullOrWhiteSpace(request.CoverImagePath) ? null : request.CoverImagePath.Trim(),
            MetadataProvider = string.IsNullOrWhiteSpace(request.MetadataProvider) ? null : request.MetadataProvider.Trim(),
            MetadataSourceUrl = string.IsNullOrWhiteSpace(request.MetadataSourceUrl) ? null : request.MetadataSourceUrl.Trim(),
            MetadataSelectionKind = string.IsNullOrWhiteSpace(request.MetadataSelectionKind) ? "Unknown" : request.MetadataSelectionKind.Trim(),
            Versions =
            {
                new PackageVersion
                {
                    VersionLabel = request.Version.VersionLabel.Trim(),
                    SupportedOs = request.Version.SupportedOs.Trim(),
                    Architecture = request.Version.Architecture,
                    InstallScriptKind = request.Version.InstallScriptKind,
                    InstallScriptPath = request.Version.InstallScriptPath.Trim(),
                    UninstallScriptPath = request.Version.UninstallScriptPath?.Trim(),
                    UninstallArguments = string.IsNullOrWhiteSpace(request.Version.UninstallArguments) ? null : request.Version.UninstallArguments.Trim(),
                    ManifestFormatVersion = manifest.FormatVersion,
                    ManifestJson = manifest.ManifestJson,
                    TimeoutSeconds = request.Version.TimeoutSeconds,
                    Notes = request.Version.Notes.Trim(),
                    InstallStrategy = request.Version.InstallStrategy.Trim(),
                    InstallerFamily = request.Version.InstallerFamily.Trim(),
                    InstallerPath = string.IsNullOrWhiteSpace(request.Version.InstallerPath) ? null : request.Version.InstallerPath.Trim(),
                    SilentArguments = string.IsNullOrWhiteSpace(request.Version.SilentArguments) ? null : request.Version.SilentArguments.Trim(),
                    InstallDiagnostics = request.Version.InstallDiagnostics.Trim(),
                    LaunchExecutablePath = string.IsNullOrWhiteSpace(request.Version.LaunchExecutablePath) ? null : request.Version.LaunchExecutablePath.Trim(),
                    Media = request.Version.Media.Select(x => new PackageMedia
                    {
                        MediaType = x.MediaType,
                        Label = x.Label.Trim(),
                        Path = x.Path.Trim(),
                        DiscNumber = x.DiscNumber,
                        EntrypointHint = string.IsNullOrWhiteSpace(x.EntrypointHint) ? null : x.EntrypointHint.Trim(),
                        SourceKind = x.SourceKind,
                        ScratchPolicy = x.ScratchPolicy
                    }).ToList(),
                    DetectionRules = request.Version.DetectionRules.Select(x => new InstallDetectionRule
                    {
                        RuleType = x.RuleType.Trim(),
                        Value = x.Value.Trim()
                    }).ToList(),
                    Prerequisites = request.Version.Prerequisites.Select(x => new PackagePrerequisite
                    {
                        Name = x.Name.Trim(),
                        Notes = x.Notes.Trim()
                    }).ToList()
                }
            }
        };

        dbContext.Packages.Add(package);
        await dbContext.SaveChangesAsync(cancellationToken);
        await normalizationService.QueuePackageAsync(package.Id, cancellationToken);
        return package.ToResponse();
    }

    public async Task<PackageResponse> UpdateMetadataAsync(Guid id, UpdatePackageMetadataRequest request, CancellationToken cancellationToken)
    {
        var package = await Query().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new AppNotFoundException("Package not found.");

        ValidationHelpers.ValidatePackageRequest(new CreatePackageRequest(
            request.Slug,
            request.Name,
            request.Description,
            request.Notes,
            request.Tags,
            request.Genres,
            request.Studio,
            request.ReleaseYear,
            request.CoverImagePath,
            package.Versions.OrderByDescending(v => v.IsActive).Select(v => new CreatePackageVersionRequest(
                v.VersionLabel,
                v.SupportedOs,
                v.Architecture,
                v.InstallScriptKind,
                v.InstallScriptPath,
                v.UninstallScriptPath,
                v.UninstallArguments,
                v.TimeoutSeconds,
                v.Notes,
                v.InstallStrategy,
                v.InstallerFamily,
                v.InstallerPath,
                v.SilentArguments,
                v.InstallDiagnostics,
                v.LaunchExecutablePath,
                v.Media.Select(m => new CreatePackageMediaRequest(m.MediaType, m.Label, m.Path, m.DiscNumber, m.EntrypointHint, m.SourceKind, m.ScratchPolicy)).ToArray(),
                v.DetectionRules.Select(d => new CreateDetectionRuleRequest(d.RuleType, d.Value)).ToArray(),
                v.Prerequisites.Select(p => new CreatePrerequisiteRequest(p.Name, p.Notes)).ToArray())).First(),
            request.MetadataProvider,
            request.MetadataSourceUrl,
            request.MetadataSelectionKind));

        var slug = request.Slug.Trim();
        var slugInUse = await dbContext.Packages.AnyAsync(p => p.Id != id && p.Slug == slug, cancellationToken);
        if (slugInUse)
        {
            throw new AppConflictException($"A package with slug '{slug}' already exists.");
        }

        package.Slug = slug;
        package.Name = request.Name.Trim();
        package.Description = request.Description.Trim();
        package.Notes = request.Notes.Trim();
        package.TagsSerialized = string.Join(';', request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        package.GenresSerialized = string.Join(';', request.Genres.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        package.Studio = request.Studio.Trim();
        package.ReleaseYear = request.ReleaseYear;
        package.CoverImagePath = string.IsNullOrWhiteSpace(request.CoverImagePath) ? null : request.CoverImagePath.Trim();
        package.MetadataProvider = string.IsNullOrWhiteSpace(request.MetadataProvider) ? null : request.MetadataProvider.Trim();
        package.MetadataSourceUrl = string.IsNullOrWhiteSpace(request.MetadataSourceUrl) ? null : request.MetadataSourceUrl.Trim();
        package.MetadataSelectionKind = string.IsNullOrWhiteSpace(request.MetadataSelectionKind) ? "Unknown" : request.MetadataSelectionKind.Trim();
        package.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return package.ToResponse();
    }

    public async Task<PackageResponse> UpdateInstallPlanAsync(Guid id, UpdatePackageInstallPlanRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidateInstallPlanRequest(request);

        var package = await Query().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new AppNotFoundException("Package not found.");

        var version = package.Versions.OrderByDescending(v => v.IsActive).First();
        version.InstallStrategy = request.InstallStrategy.Trim();
        version.InstallerFamily = request.InstallerFamily.Trim();
        version.InstallerPath = string.IsNullOrWhiteSpace(request.InstallerPath) ? null : request.InstallerPath.Trim();
        version.SilentArguments = string.IsNullOrWhiteSpace(request.SilentArguments) ? null : request.SilentArguments.Trim();
        version.InstallDiagnostics = request.InstallDiagnostics.Trim();
        version.LaunchExecutablePath = string.IsNullOrWhiteSpace(request.LaunchExecutablePath) ? null : request.LaunchExecutablePath.Trim();
        version.UninstallScriptPath = string.IsNullOrWhiteSpace(request.UninstallScriptPath) ? null : request.UninstallScriptPath.Trim();
        version.UninstallArguments = string.IsNullOrWhiteSpace(request.UninstallArguments) ? null : request.UninstallArguments.Trim();
        version.InstallScriptKind = InstallScriptKind.PowerShell;
        version.InstallScriptPath = ResolveInstallScriptPath(version.InstallStrategy);

        var existingRules = version.DetectionRules.ToList();
        if (existingRules.Count > 0)
        {
            dbContext.InstallDetectionRules.RemoveRange(existingRules);
        }

        version.DetectionRules.Clear();
        var replacementRules = request.DetectionRules.Select(rule => new InstallDetectionRule
        {
            PackageVersionId = version.Id,
            RuleType = rule.RuleType.Trim(),
            Value = rule.Value.Trim()
        }).ToList();
        version.DetectionRules = replacementRules;
        dbContext.InstallDetectionRules.AddRange(replacementRules);

        var manifest = PackageManifestBuilder.Build(new CreatePackageRequest(
            package.Slug,
            package.Name,
            package.Description,
            package.Notes,
            package.Tags.ToArray(),
            package.Genres.ToArray(),
            package.Studio,
            package.ReleaseYear,
            package.CoverImagePath,
            new CreatePackageVersionRequest(
                version.VersionLabel,
                version.SupportedOs,
                version.Architecture,
                version.InstallScriptKind,
                version.InstallScriptPath,
                version.UninstallScriptPath,
                version.UninstallArguments,
                version.TimeoutSeconds,
                version.Notes,
                version.InstallStrategy,
                version.InstallerFamily,
                version.InstallerPath,
                version.SilentArguments,
                version.InstallDiagnostics,
                version.LaunchExecutablePath,
                version.Media.Select(m => new CreatePackageMediaRequest(m.MediaType, m.Label, m.Path, m.DiscNumber, m.EntrypointHint, m.SourceKind, m.ScratchPolicy)).ToArray(),
                version.DetectionRules.Select(d => new CreateDetectionRuleRequest(d.RuleType, d.Value)).ToArray(),
                version.Prerequisites.Select(p => new CreatePrerequisiteRequest(p.Name, p.Notes)).ToArray()),
            package.MetadataProvider,
            package.MetadataSourceUrl,
            package.MetadataSelectionKind));

        version.ManifestFormatVersion = manifest.FormatVersion;
        version.ManifestJson = manifest.ManifestJson;
        version.ProcessingState = "Normalizing";
        version.NormalizationDiagnostics = "Install plan updated. Waiting to rebuild normalized asset.";
        package.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await normalizationService.QueuePackageVersionAsync(package.Id, version.Id, cancellationToken);
        return package.ToResponse();
    }

    public async Task<PackageResponse> ArchiveAsync(Guid id, string? reason, CancellationToken cancellationToken)
    {
        var package = await Query().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new AppNotFoundException("Package not found.");

        package.IsArchived = true;
        package.ArchivedReason = string.IsNullOrWhiteSpace(reason) ? "Archived from library cleanup." : reason.Trim();
        package.ArchivedAtUtc = DateTimeOffset.UtcNow;
        package.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return package.ToResponse();
    }

    public async Task<PackageResponse> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var package = await Query().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new AppNotFoundException("Package not found.");

        package.IsArchived = false;
        package.ArchivedReason = null;
        package.ArchivedAtUtc = null;
        package.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return package.ToResponse();
    }

    private static string ResolveInstallScriptPath(string installStrategy) =>
        installStrategy switch
        {
            "PortableCopy" => "builtin:portable-copy",
            "AutoInstall" => "builtin:auto-install",
            _ => "builtin:needs-review"
        };

    public async Task<IReadOnlyCollection<PackageResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var packages = await Query().ToListAsync(cancellationToken);
        return packages.Select(p => p.ToResponse()).ToArray();
    }

    public async Task<PackageResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var package = await Query().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return package?.ToResponse();
    }

    public async Task<PackageResponse> ReNormalizeAsync(Guid id, CancellationToken cancellationToken)
    {
        var package = await Query().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new AppNotFoundException("Package not found.");

        var version = package.Versions.OrderByDescending(v => v.IsActive).First();
        version.ProcessingState = "Normalizing";
        version.NormalizationDiagnostics = "Manually queued for re-normalization.";
        package.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await normalizationService.QueuePackageVersionAsync(package.Id, version.Id, cancellationToken);
        return package.ToResponse();
    }

    public async Task<int> BulkReNormalizeNeedsReviewAsync(CancellationToken cancellationToken)
    {
        var packages = await Query()
            .Where(p => !p.IsArchived)
            .ToListAsync(cancellationToken);

        var targets = packages
            .Where(p =>
            {
                var version = p.Versions.OrderByDescending(v => v.IsActive).FirstOrDefault();
                return version is not null &&
                       string.Equals(version.ProcessingState, "NeedsReview", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        foreach (var package in targets)
        {
            var version = package.Versions.OrderByDescending(v => v.IsActive).First();
            version.ProcessingState = "Normalizing";
            version.NormalizationDiagnostics = "Queued for bulk re-normalization.";
            package.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var package in targets)
        {
            var version = package.Versions.OrderByDescending(v => v.IsActive).First();
            await normalizationService.QueuePackageVersionAsync(package.Id, version.Id, cancellationToken);
        }

        return targets.Count;
    }

    private IQueryable<Package> Query() =>
        dbContext.Packages
            .Include(p => p.Versions).ThenInclude(v => v.Media)
            .Include(p => p.Versions).ThenInclude(v => v.DetectionRules)
            .Include(p => p.Versions).ThenInclude(v => v.Prerequisites)
            .OrderBy(p => p.Name);
}
