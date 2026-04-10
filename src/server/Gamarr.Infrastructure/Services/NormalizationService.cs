using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Gamarr.Application.Services;
using Gamarr.Domain.Entities;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Models;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gamarr.Infrastructure.Services;

public sealed class NormalizationService(
    GamarrDbContext dbContext) : INormalizationService
{
    private static readonly TimeSpan StaleRunningThreshold = TimeSpan.FromMinutes(2);

    public async Task QueuePackageAsync(Guid packageId, CancellationToken cancellationToken)
    {
        var package = await dbContext.Packages
            .Include(p => p.Versions)
            .FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken);

        var version = package?.Versions.OrderByDescending(v => v.IsActive).FirstOrDefault();
        if (package is null || version is null)
        {
            return;
        }

        await QueuePackageVersionAsync(package.Id, version.Id, cancellationToken);
    }

    public async Task QueuePackageVersionAsync(Guid packageId, Guid packageVersionId, CancellationToken cancellationToken)
    {
        var version = await dbContext.PackageVersions
            .Include(v => v.Media)
            .FirstOrDefaultAsync(v => v.Id == packageVersionId && v.PackageId == packageId, cancellationToken);

        if (version is null)
        {
            return;
        }

        var existing = await dbContext.NormalizationJobs
            .Where(j => j.PackageVersionId == packageVersionId && (j.State == "Queued" || j.State == "Running"))
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (existing.State == "Queued")
            {
                return;
            }

            var staleCutoff = DateTimeOffset.UtcNow.Subtract(StaleRunningThreshold);
            if (existing.StartedAtUtc.HasValue && existing.StartedAtUtc.Value >= staleCutoff)
            {
                return;
            }

            existing.State = "Failed";
            existing.Summary = "Normalization job was abandoned before completion.";
            existing.ErrorMessage ??= "A previous normalization worker stopped before finishing this job.";
            existing.CompletedAtUtc = DateTimeOffset.UtcNow;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        var sourcePath = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).Select(m => m.Path).FirstOrDefault() ?? string.Empty;
        dbContext.NormalizationJobs.Add(new NormalizationJob
        {
            PackageId = packageId,
            PackageVersionId = packageVersionId,
            State = "Queued",
            SourcePath = sourcePath,
            Summary = "Waiting to normalize package media.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        version.ProcessingState = "Normalizing";
        version.NormalizationDiagnostics = "Queued for background normalization.";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<NormalizationJobResponse>> ListJobsAsync(Guid? packageId, string? state, CancellationToken cancellationToken)
    {
        var jobs = await dbContext.NormalizationJobs
            .Include(j => j.Package)
            .Include(j => j.PackageVersion)
            .Where(j => (!packageId.HasValue || j.PackageId == packageId.Value) &&
                        (string.IsNullOrWhiteSpace(state) || j.State == state))
            .OrderByDescending(j => j.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return jobs.Select(j => new NormalizationJobResponse(
            j.Id,
            j.PackageId,
            j.PackageVersionId,
            j.Package?.Name ?? string.Empty,
            j.PackageVersion?.VersionLabel ?? string.Empty,
            j.State,
            j.SourcePath,
            j.Summary,
            j.ErrorMessage,
            j.CreatedAtUtc,
            j.StartedAtUtc,
            j.CompletedAtUtc,
            j.UpdatedAtUtc)).ToArray();
    }
}

public sealed class NormalizationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<NormalizationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StaleRunningThreshold = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Normalization worker iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessNextJobAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GamarrDbContext>();

        await RecoverStaleJobsAsync(dbContext, cancellationToken);

        var job = await dbContext.NormalizationJobs
            .Include(j => j.Package)
            .Include(j => j.PackageVersion!).ThenInclude(v => v.Media)
            .Include(j => j.PackageVersion!).ThenInclude(v => v.DetectionRules)
            .Where(j => j.State == "Queued")
            .OrderBy(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (job?.Package is null || job.PackageVersion is null)
        {
            return;
        }

        job.State = "Running";
        job.StartedAtUtc = DateTimeOffset.UtcNow;
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        job.Summary = "Analyzing source media.";
        job.PackageVersion.ProcessingState = "Normalizing";
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var result = Normalize(job.Package, job.PackageVersion, cancellationToken);

            job.State = result.State;
            job.Summary = result.Summary;
            job.ErrorMessage = result.ErrorMessage;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;

            job.PackageVersion.ProcessingState = result.VersionState;
            job.PackageVersion.NormalizedAssetRootPath = result.NormalizedAssetRootPath;
            job.PackageVersion.NormalizedAtUtc = result.NormalizedAtUtc;
            job.PackageVersion.NormalizationDiagnostics = result.Diagnostics;
            job.PackageVersion.InstallStrategy = result.InstallStrategy;
            job.PackageVersion.InstallerFamily = result.InstallerFamily;
            job.PackageVersion.InstallerPath = result.InstallerPath;
            job.PackageVersion.SilentArguments = result.SilentArguments;
            if (!string.IsNullOrWhiteSpace(result.LaunchExecutablePath))
            {
                job.PackageVersion.LaunchExecutablePath = result.LaunchExecutablePath;
            }
            job.PackageVersion.InstallScriptKind = result.InstallScriptKind;
            job.PackageVersion.InstallScriptPath = result.InstallScriptPath;
            job.PackageVersion.InstallDiagnostics = result.InstallDiagnostics;

            if (result.ReplaceDetectionRules)
            {
                var existingRules = job.PackageVersion.DetectionRules.ToList();
                if (existingRules.Count > 0)
                {
                    dbContext.InstallDetectionRules.RemoveRange(existingRules);
                }

                var replacementRules = result.DetectionRules.Select(rule => new InstallDetectionRule
                {
                    PackageVersionId = job.PackageVersion.Id,
                    RuleType = rule.RuleType,
                    Value = rule.Value
                }).ToList();

                job.PackageVersion.DetectionRules = replacementRules;
                dbContext.InstallDetectionRules.AddRange(replacementRules);
            }

            var manifest = PackageManifestBuilder.Build(new CreatePackageRequest(
                job.Package.Slug,
                job.Package.Name,
                job.Package.Description,
                job.Package.Notes,
                job.Package.Tags.ToArray(),
                job.Package.Genres.ToArray(),
                job.Package.Studio,
                job.Package.ReleaseYear,
                job.Package.CoverImagePath,
                new CreatePackageVersionRequest(
                    job.PackageVersion.VersionLabel,
                    job.PackageVersion.SupportedOs,
                    job.PackageVersion.Architecture,
                    job.PackageVersion.InstallScriptKind,
                    job.PackageVersion.InstallScriptPath,
                    job.PackageVersion.UninstallScriptPath,
                    job.PackageVersion.UninstallArguments,
                    job.PackageVersion.TimeoutSeconds,
                    job.PackageVersion.Notes,
                    job.PackageVersion.InstallStrategy,
                    job.PackageVersion.InstallerFamily,
                    job.PackageVersion.InstallerPath,
                    job.PackageVersion.SilentArguments,
                    job.PackageVersion.InstallDiagnostics,
                    job.PackageVersion.LaunchExecutablePath,
                    job.PackageVersion.Media.Select(m => new CreatePackageMediaRequest(m.MediaType, m.Label, m.Path, m.DiscNumber, m.EntrypointHint, m.SourceKind, m.ScratchPolicy)).ToArray(),
                    job.PackageVersion.DetectionRules.Select(d => new CreateDetectionRuleRequest(d.RuleType, d.Value)).ToArray(),
                    job.PackageVersion.Prerequisites.Select(p => new CreatePrerequisiteRequest(p.Name, p.Notes)).ToArray()),
                job.Package.MetadataProvider,
                job.Package.MetadataSourceUrl,
                job.Package.MetadataSelectionKind));

            job.PackageVersion.ManifestFormatVersion = manifest.FormatVersion;
            job.PackageVersion.ManifestJson = manifest.ManifestJson;
            job.Package.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Normalization failed for package {PackageId}", job.PackageId);
            job.State = "Failed";
            job.Summary = "Normalization failed.";
            job.ErrorMessage = ex.Message;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;
            job.PackageVersion.ProcessingState = "Failed";
            job.PackageVersion.NormalizationDiagnostics = ex.Message;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RecoverStaleJobsAsync(GamarrDbContext dbContext, CancellationToken cancellationToken)
    {
        var staleCutoff = DateTimeOffset.UtcNow.Subtract(StaleRunningThreshold);
        var staleJobs = await dbContext.NormalizationJobs
            .Include(j => j.PackageVersion)
            .Where(j => j.State == "Running" &&
                        j.StartedAtUtc.HasValue &&
                        j.StartedAtUtc.Value < staleCutoff)
            .ToListAsync(cancellationToken);

        if (staleJobs.Count == 0)
        {
            return;
        }

        foreach (var staleJob in staleJobs)
        {
            staleJob.State = "Queued";
            staleJob.Summary = "Retrying a stale normalization job.";
            staleJob.ErrorMessage = null;
            staleJob.StartedAtUtc = null;
            staleJob.CompletedAtUtc = null;
            staleJob.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (staleJob.PackageVersion is not null)
            {
                staleJob.PackageVersion.ProcessingState = "Normalizing";
                staleJob.PackageVersion.NormalizationDiagnostics = "Retrying a stale normalization job.";
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static NormalizationResult Normalize(
        Package package,
        PackageVersion version,
        CancellationToken cancellationToken)
    {
        var sources = version.Media.OrderBy(m => m.DiscNumber ?? int.MaxValue).ThenBy(m => m.Label).ToArray();
        var source = sources.FirstOrDefault();
        if (source is null)
        {
            return NormalizationResult.Failed("No source media is registered for this package.");
        }

        // Check for a gamarr.json hint file — if present, apply it and skip all further analysis.
        var hintFolder = Directory.Exists(source.Path) ? source.Path : (Path.GetDirectoryName(source.Path) ?? "");
        var hint = TryReadHintFile(hintFolder);
        if (hint is not null)
        {
            return NormalizationResult.FromHint(hint, source.Path);
        }

        // Multi-disc sets — agent mounts originals at job time.
        if (sources.Length > 1 && sources.All(IsDiskImageSource))
        {
            return NormalizationResult.DiskImages(
                $"Multi-disc set of {sources.Length} disc image(s) — agent will mount and detect installer at install time.");
        }

        // Installer folder — point agent directly at source, no copy.
        if (Directory.Exists(source.Path))
        {
            return NormalizeDirectory(source.Path, cancellationToken);
        }

        if (File.Exists(source.Path))
        {
            var extension = Path.GetExtension(source.Path).ToLowerInvariant();
            var sourceFolder = Path.GetDirectoryName(source.Path) ?? source.Path;

            if (extension == ".msi")
            {
                return NormalizationResult.Msi(sourceFolder, Path.GetFileName(source.Path), "Detected standalone MSI installer in library.");
            }

            if (extension == ".zip")
            {
                // Extract to temp for inspection only — the ZIP remains in the library.
                return NormalizeZip(source.Path, cancellationToken);
            }

            if (extension == ".iso")
            {
                return NormalizeIso(source.Path, cancellationToken);
            }

            if (IsDiskImageSource(source))
            {
                return NormalizationResult.DiskImages(
                    $"Disc image source '{Path.GetFileName(source.Path)}' — agent will mount and detect installer at install time.");
            }

            // Unknown single file — let agent attempt install from its containing folder.
            return NormalizationResult.AutoInstaller(
                sourceFolder,
                "Unknown",
                Path.GetFileName(source.Path),
                null,
                $"Source '{Path.GetFileName(source.Path)}' type not pre-classified — agent will attempt installer detection at install time.");
        }

        return NormalizationResult.Failed($"Source '{source.Path}' could not be found.");
    }


    private static NormalizationResult NormalizeIso(string imagePath, CancellationToken cancellationToken)
    {
        // Mount the ISO for inspection only — no files are copied. The agent mounts the original at job time.
        string? mountedRoot = null;
        try
        {
            mountedRoot = MountIso(imagePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(mountedRoot) || !Directory.Exists(mountedRoot))
            {
                return NormalizationResult.DiskImages(
                    $"ISO '{Path.GetFileName(imagePath)}' mounted but could not be inspected — agent will mount and detect at install time.");
            }

            var installerCandidate = InspectInstallerDirectory(mountedRoot);

            if (installerCandidate is null)
            {
                return NormalizationResult.DiskImages(
                    $"No installer pre-identified in '{Path.GetFileName(imagePath)}' — agent will auto-detect at install time.");
            }

            if (string.Equals(installerCandidate.InstallerFamily, "Msi", StringComparison.OrdinalIgnoreCase) &&
                HasBootstrapInstaller(mountedRoot))
            {
                return NormalizationResult.DiskImages(
                    $"ISO '{Path.GetFileName(imagePath)}' has bootstrap installer — agent will detect the right entrypoint at install time.");
            }

            // Pass pre-detected installer hints to agent — it will use them when the ISO is mounted at job time.
            return NormalizationResult.DiskImages(
                $"Pre-classified installer in '{Path.GetFileName(imagePath)}' as {installerCandidate.InstallerFamily}.",
                installerFamily: installerCandidate.InstallerFamily,
                installerPath: installerCandidate.InstallerRelativePath,
                silentArguments: installerCandidate.SilentArguments);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(mountedRoot))
                DismountIso(imagePath, cancellationToken);
        }
    }

    private static NormalizationResult NormalizeZip(string zipPath, CancellationToken cancellationToken)
    {
        // Extract to a temp location for inspection only, then delete the temp folder.
        var tempRoot = Path.Combine(Path.GetTempPath(), $"gamarr-zip-inspect-{Guid.NewGuid():N}");
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempRoot);
            var result = NormalizeDirectory(tempRoot, cancellationToken, extractedFrom: zipPath);
            // Rebase NormalizedAssetRootPath to null — agent will extract at job time.
            return result with { NormalizedAssetRootPath = null };
        }
        catch (Exception ex)
        {
            return NormalizationResult.AutoInstaller(null, "Unknown", null, null,
                $"ZIP inspection failed ({ex.Message}) — agent will extract and detect at install time.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }


    private static NormalizationResult NormalizeDirectory(string sourceRoot, CancellationToken cancellationToken, string? extractedFrom = null)
    {
        // Point the agent directly at the source folder — nothing is copied.
        var allFiles = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray();
        var label = extractedFrom is not null
            ? $"Inspected ZIP '{Path.GetFileName(extractedFrom)}'"
            : $"Scanned folder '{Path.GetFileName(sourceRoot)}'";

        var msiCandidates = allFiles
            .Where(path => string.Equals(Path.GetExtension(path), ".msi", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                RelativePath = Path.GetRelativePath(sourceRoot, path),
                Score = ScoreInstallerMediaCandidate(sourceRoot, path)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.RelativePath.Length)
            .ToArray();

        if (msiCandidates.Length > 0)
        {
            var selected = msiCandidates[0];
            return NormalizationResult.Msi(sourceRoot, selected.RelativePath, $"{label} — detected MSI installer.");
        }

        var hasInstallerSignals = allFiles.Any(path => ScoreInstallerMediaCandidate(sourceRoot, path) > 0) ||
                                  File.Exists(Path.Combine(sourceRoot, "Autorun.inf"));

        var exeCandidates = allFiles
            .Where(path => string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Score = ScorePortableExecutable(sourceRoot, path) })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path.Length)
            .ToArray();

        if (exeCandidates.Length > 0)
        {
            var relativePath = Path.GetRelativePath(sourceRoot, exeCandidates[0].Path);
            return NormalizationResult.Portable(sourceRoot, relativePath, $"{label} — detected portable executable.");
        }

        if (hasInstallerSignals)
        {
            return NormalizationResult.AutoInstaller(sourceRoot, "Unknown", null, null,
                $"{label} — found installer signals; agent will detect at install time.");
        }

        return NormalizationResult.AutoInstaller(sourceRoot, "Unknown", null, null,
            $"{label} — could not pre-classify; agent will attempt auto-detection at install time.");
    }

    private static int ScorePortableExecutable(string root, string path)
    {
        var fileName = Path.GetFileName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var lower = fileName.ToLowerInvariant();
        var relative = Path.GetRelativePath(root, path);
        var relativeLower = relative.ToLowerInvariant();
        var segments = relativeLower.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (segments.Any(IsIgnoredPortableSegment))
        {
            return -100;
        }

        if (lower.Contains("setup") || lower.Contains("install") || lower.Contains("autorun") || lower.Contains("unins") || lower.Contains("uninstall") || lower.Contains("redist") || lower.Contains("dxsetup"))
        {
            return -100;
        }

        var depth = relative.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
        var score = 100 - (depth * 10);
        if (name.Equals("game", StringComparison.OrdinalIgnoreCase) || name.Equals("launcher", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static int ScoreInstallerMediaCandidate(string root, string path)
    {
        var fileName = Path.GetFileName(path);
        var lower = fileName.ToLowerInvariant();
        var relative = Path.GetRelativePath(root, path);
        var relativeLower = relative.ToLowerInvariant();
        var segments = relativeLower.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (segments.Any(IsIgnoredPortableSegment))
        {
            return -100;
        }

        if (string.Equals(Path.GetExtension(path), ".msi", StringComparison.OrdinalIgnoreCase))
        {
            var depth = relative.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
            var score = 200 - (depth * 20);

            if (depth == 0)
            {
                score += 50;
            }

            if (lower.Contains("directx") || lower.Contains("redist") || lower.Contains("vc"))
            {
                score -= 150;
            }

            return score;
        }

        if (lower.Contains("autorun") || lower.Contains("setup") || lower.Contains("install"))
        {
            var depth = relative.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
            return 100 - (depth * 20);
        }

        return 0;
    }

    private static bool IsIgnoredPortableSegment(string segment) =>
        segment is "support" or "redist" or "redistributables" or "directx9c" or "vc80_redist" or "tools" or "punkbustersvc" or "keygen" or "crack" or "nocd";

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

        return inspectionCandidates
            .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => (Inspection: InspectInstallerFile(sourcePath, candidate.Path, candidate.DiagnosticPrefix), candidate.Priority))
            .OrderByDescending(candidate => candidate.Priority)
            .ThenByDescending(candidate => GetInstallerSafetyScore(candidate.Inspection))
            .ThenBy(candidate => candidate.Inspection.InstallerRelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Inspection)
            .FirstOrDefault();
    }

    private static InstallerInspectionResult InspectInstallerFile(string sourcePath, string installerPath, string diagnosticPrefix)
    {
        var family = DetectInstallerFamily(installerPath);
        var relativePath = Path.GetRelativePath(sourcePath, installerPath).Replace(Path.DirectorySeparatorChar, '\\');
        var isSafe = family is "Msi" or "Inno" or "Nsis" || (family is "InstallShield" && IsTrustedInstallShield(installerPath)) || family is "Unknown";
        var silentArgs = family switch
        {
            "Msi" => "/qn /norestart",
            "Inno" => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
            "Nsis" => "/S",
            "InstallShield" => "/s",
            _ => null
        };

        return new InstallerInspectionResult(
            isSafe ? "AutoInstall" : "NeedsReview",
            family,
            relativePath,
            silentArgs,
            isSafe
                ? $"{diagnosticPrefix} Classified installer family as {family}."
                : $"{diagnosticPrefix} Installer family could not be safely automated.");
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

    private static bool HasBootstrapInstaller(string sourcePath) =>
        File.Exists(Path.Combine(sourcePath, "setup.exe")) ||
        File.Exists(Path.Combine(sourcePath, "Setup.exe")) ||
        File.Exists(Path.Combine(sourcePath, "autorun.exe")) ||
        File.Exists(Path.Combine(sourcePath, "Autorun.exe"));

    private static bool IsTrustedInstallShield(string installerPath)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(installerPath);
            var fileVersion = version.FileVersion?.Trim();
            if (!string.IsNullOrWhiteSpace(fileVersion))
            {
                var majorText = fileVersion.Split('.', ',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (int.TryParse(majorText, out var major))
                {
                    return major >= 6;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static string DetectInstallerFamily(string installerPath)
    {
        if (string.Equals(Path.GetExtension(installerPath), ".msi", StringComparison.OrdinalIgnoreCase))
        {
            return "Msi";
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(installerPath);
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
                var ascii = Encoding.Latin1.GetString(buffer);
                var unicode = Encoding.Unicode.GetString(buffer);
                var probe = string.Concat(ascii, " ", unicode);

                if (probe.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase))
                {
                    return "Inno";
                }

                if (probe.Contains("Nullsoft", StringComparison.OrdinalIgnoreCase) || probe.Contains("NSIS", StringComparison.OrdinalIgnoreCase))
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

    private static bool IsDiskImageSource(PackageMedia media)
    {
        var extension = Path.GetExtension(media.Path);
        return media.MediaType is MediaType.Iso or MediaType.DiskImage ||
               media.SourceKind is PackageSourceKind.MountedVolume or PackageSourceKind.ExtractedWorkspace ||
               extension is ".iso" or ".img" or ".bin" or ".cue" or ".mdf" or ".mds" or ".nrg" or ".ccd" or ".cdi";
    }


    private static string MountIso(string imagePath, CancellationToken cancellationToken)
    {
        var escapedImagePath = EscapePowerShellString(imagePath);
        var script = $@"
$mount = Get-DiskImage -ImagePath '{escapedImagePath}' -ErrorAction SilentlyContinue
if (-not $mount -or -not $mount.Attached) {{
  $mount = Mount-DiskImage -ImagePath '{escapedImagePath}' -PassThru
}}

if (-not $mount) {{
  throw ""Unable to access mounted image '{escapedImagePath}'.""
}}

$volume = $null
for ($attempt = 0; $attempt -lt 10 -and -not $volume; $attempt++) {{
  Start-Sleep -Milliseconds 500
  $volume = $mount | Get-Volume | Where-Object {{ $_.DriveLetter }} | Select-Object -First 1
}}

if (-not $volume) {{
  throw ""Mounted ISO '{escapedImagePath}' does not expose a drive letter.""
}}

Write-Output ($volume.DriveLetter + ':\')
";

        return ExecutePowerShell(script, cancellationToken).Trim();
    }

    private static void DismountIso(string imagePath, CancellationToken cancellationToken)
    {
        var script = $"Dismount-DiskImage -ImagePath '{EscapePowerShellString(imagePath)}' -ErrorAction SilentlyContinue";
        _ = ExecutePowerShell(script, cancellationToken);
    }

    private static string ExecutePowerShell(string script, CancellationToken cancellationToken)
    {
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"gamarr-normalize-{Guid.NewGuid():N}.ps1");

        try
        {
            File.WriteAllText(tempScriptPath, script);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                    ? $"PowerShell command failed with exit code {process.ExitCode}."
                    : stderr.Trim());
            }

            return stdout;
        }
        finally
        {
            if (File.Exists(tempScriptPath))
            {
                File.Delete(tempScriptPath);
            }
        }
    }

    private static string EscapePowerShellString(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);


    private sealed record NormalizationDetectionRule(string RuleType, string Value);

    private sealed record NormalizationResult(
        string State,
        string VersionState,
        string Summary,
        string? ErrorMessage,
        string? NormalizedAssetRootPath,
        DateTimeOffset? NormalizedAtUtc,
        string Diagnostics,
        string InstallStrategy,
        string InstallerFamily,
        string? InstallerPath,
        string? SilentArguments,
        string? LaunchExecutablePath,
        InstallScriptKind InstallScriptKind,
        string InstallScriptPath,
        string InstallDiagnostics,
        bool ReplaceDetectionRules,
        IReadOnlyCollection<NormalizationDetectionRule> DetectionRules)
    {
        public static NormalizationResult Portable(string root, string launchRelativePath, string diagnostics) =>
            new(
                "Completed",
                "Ready",
                "Portable source normalized successfully.",
                null,
                root,
                DateTimeOffset.UtcNow,
                diagnostics,
                "PortableDirect",
                "Portable",
                null,
                null,
                Path.Combine(root, launchRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                InstallScriptKind.PowerShell,
                "builtin:portable-copy",
                diagnostics,
                true,
                [new NormalizationDetectionRule("FileExists", Path.Combine(root, launchRelativePath.Replace('/', Path.DirectorySeparatorChar)))]);

        public static NormalizationResult Msi(string root, string installerFileName, string diagnostics) =>
            new(
                "Completed",
                "Ready",
                "MSI source normalized successfully.",
                null,
                root,
                DateTimeOffset.UtcNow,
                diagnostics,
                "AutoInstall",
                "Msi",
                installerFileName,
                "/qn /norestart",
                null,
                InstallScriptKind.PowerShell,
                "builtin:auto-install",
                diagnostics,
                true,
                [new NormalizationDetectionRule("UninstallEntryExists", Path.GetFileNameWithoutExtension(installerFileName))]);

        /// <summary>
        /// Use for disc image sources (ISO, BIN/CUE, MDF etc.) — NormalizedAssetRootPath is null
        /// so the agent receives original media paths and mounts them at job execution time.
        /// Optional installer hints from pre-inspection are forwarded to the agent via manifest fields.
        /// </summary>
        public static NormalizationResult DiskImages(
            string diagnostics,
            string? installerFamily = null,
            string? installerPath = null,
            string? silentArguments = null) =>
            new(
                "Completed",
                "Ready",
                "Disk image sources ready — agent will mount and detect installer at install time.",
                null,
                null,   // NormalizedAssetRootPath intentionally null: agent uses original media paths
                DateTimeOffset.UtcNow,
                diagnostics,
                "AutoInstall",
                installerFamily ?? "Unknown",
                installerPath,
                silentArguments,
                null,
                InstallScriptKind.PowerShell,
                "builtin:auto-install",
                diagnostics,
                true,
                [new NormalizationDetectionRule("FileExists", @"%INSTALL_ROOT%\installed\gamarr-library-import.marker")]);

        public static NormalizationResult AutoInstaller(string? root, string installerFamily, string? installerPath, string? silentArguments, string diagnostics) =>
            new(
                "Completed",
                "Ready",
                $"{installerFamily} source normalized successfully.",
                null,
                root,
                DateTimeOffset.UtcNow,
                diagnostics,
                "AutoInstall",
                installerFamily,
                installerPath,
                silentArguments,
                null,
                InstallScriptKind.PowerShell,
                "builtin:auto-install",
                diagnostics,
                true,
                [new NormalizationDetectionRule("FileExists", @"%INSTALL_ROOT%\installed\gamarr-library-import.marker")]);

        public static NormalizationResult NeedsReviewWithAsset(string root, string diagnostics) =>
            AutoInstaller(root, "Unknown", null, null, diagnostics);

        /// <summary>
        /// Builds a result directly from a gamarr.json hint file, bypassing analysis.
        /// NormalizedAssetRootPath is set to the source folder for folder/exe sources and null for disc images.
        /// </summary>
        public static NormalizationResult FromHint(GamarrHint hint, string sourcePath)
        {
            var isDiskImage = IsDiskImageExtension(Path.GetExtension(sourcePath));
            var sourceFolder = Directory.Exists(sourcePath) ? sourcePath : (Path.GetDirectoryName(sourcePath) ?? "");
            var normalizedRoot = isDiskImage ? null : sourceFolder;

            IReadOnlyCollection<NormalizationDetectionRule> rules = hint.InstallDetection is not null
                ? [new NormalizationDetectionRule(hint.InstallDetection.Type, hint.InstallDetection.Value)]
                : [new NormalizationDetectionRule("FileExists", @"%INSTALL_ROOT%\installed\gamarr-library-import.marker")];

            return new NormalizationResult(
                "Completed",
                "Ready",
                "Hint file (gamarr.json) applied — analysis skipped.",
                null,
                normalizedRoot,
                DateTimeOffset.UtcNow,
                "Hint file (gamarr.json) was present — metadata applied directly.",
                isDiskImage ? "AutoInstall" : (hint.InstallerPath is not null ? "AutoInstall" : "AutoInstall"),
                hint.InstallerFamily ?? "Unknown",
                hint.InstallerPath,
                hint.SilentArgs,
                hint.LaunchPath,
                InstallScriptKind.PowerShell,
                "builtin:auto-install",
                "Hint file applied.",
                true,
                rules);
        }

        private static bool IsDiskImageExtension(string ext) =>
            ext is ".iso" or ".img" or ".bin" or ".cue" or ".mdf" or ".mds" or ".nrg" or ".ccd" or ".cdi";

        public static NormalizationResult NeedsReview(string diagnostics) =>
            new(
                "NeedsReview",
                "NeedsReview",
                "Normalization needs manual review.",
                null,
                null,
                null,
                diagnostics,
                "NeedsReview",
                "Unknown",
                null,
                null,
                null,
                InstallScriptKind.PowerShell,
                "builtin:needs-review",
                diagnostics,
                false,
                Array.Empty<NormalizationDetectionRule>());

        public static NormalizationResult Failed(string diagnostics) =>
            new(
                "Failed",
                "Failed",
                "Normalization failed.",
                diagnostics,
                null,
                null,
                diagnostics,
                "NeedsReview",
                "Unknown",
                null,
                null,
                null,
                InstallScriptKind.PowerShell,
                "builtin:needs-review",
                diagnostics,
                false,
                Array.Empty<NormalizationDetectionRule>());
    }

    private sealed record InstallerInspectionResult(
        string Strategy,
        string InstallerFamily,
        string InstallerRelativePath,
        string? SilentArguments,
        string Diagnostics);

    /// <summary>
    /// Reads a gamarr.json hint file from the given folder.
    /// Returns null if the file is absent or cannot be parsed.
    /// </summary>
    private static GamarrHint? TryReadHintFile(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        var hintPath = Path.Combine(folderPath, "gamarr.json");
        if (!File.Exists(hintPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(hintPath);
            return System.Text.Json.JsonSerializer.Deserialize<GamarrHint>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Deserialized from gamarr.json — a per-game hint file the user drops in their library folder.</summary>
public sealed class GamarrHint
{
    public string? Name { get; init; }
    public string? InstallerPath { get; init; }
    public string? InstallerFamily { get; init; }
    public string? SilentArgs { get; init; }
    public string? LaunchPath { get; init; }
    public string? UninstallPath { get; init; }
    public string? UninstallArgs { get; init; }
    public GamarrHintDetection? InstallDetection { get; init; }
}

public sealed class GamarrHintDetection
{
    public string Type { get; init; } = "FileExists";
    public string Value { get; init; } = string.Empty;
}
