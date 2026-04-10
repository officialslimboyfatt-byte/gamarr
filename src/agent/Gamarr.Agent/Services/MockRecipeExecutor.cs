using Gamarr.Agent.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gamarr.Agent.Services;

[SupportedOSPlatform("windows")]
public sealed class MockRecipeExecutor : IPackageJobExecutor
{
    private static readonly string GamarrRoot = AgentPathResolver.GetWritableGamarrRoot();
    private static readonly string MockInstallRoot = Path.Combine(GamarrRoot, "MockInstalls");

    public async Task ExecuteAsync(Guid machineId, NextJobResponse job, Func<JobEventRequest, Task> reportEvent, CancellationToken cancellationToken)
    {
        if (job.ActionType == JobActionType.Launch)
        {
            await ExecuteLaunchAsync(machineId, job, reportEvent, cancellationToken);
            return;
        }

        if (job.ActionType == JobActionType.Validate)
        {
            await ExecuteValidateAsync(machineId, job, reportEvent, cancellationToken);
            return;
        }

        if (job.ActionType == JobActionType.Uninstall)
        {
            await ExecuteUninstallAsync(machineId, job, reportEvent, cancellationToken);
            return;
        }

        await ReportAsync(
            reportEvent,
            3,
            JobState.Preparing,
            $"Loaded manifest {job.ManifestFormatVersion} for {job.PackageName} {job.PackageVersionLabel}.",
            new JobLogRequest(LogLevelKind.Information, "agent", "Package manifest received by agent.", job.ManifestJson),
            cancellationToken);

        var executionRoot = BuildExecutionRoot(machineId, job.PackageId);
        Directory.CreateDirectory(executionRoot);
        await CleanupStaleScratchAsync(executionRoot, cancellationToken);

        await using var mediaSession = await PrepareMediaAsync(job, executionRoot, cancellationToken);

        await ReportAsync(
            reportEvent,
            4,
            JobState.Mounting,
            $"Prepared {mediaSession.Media.Count} media reference(s).",
            mediaSession.Media.Select(media => new JobLogRequest(
                LogLevelKind.Information,
                "media",
                $"{media.Label}: source={media.SourcePath} root={media.RootPath} method={media.PrepareMethod} sourceKind={media.SourceKind} scratch={media.ScratchPolicy} disc={(media.DiscNumber?.ToString() ?? "-")}",
                null)).ToArray(),
            cancellationToken);

        IReadOnlyCollection<JobLogRequest> installLogs = job.InstallScriptKind switch
        {
            InstallScriptKind.MockRecipe =>
                [
                    new JobLogRequest(LogLevelKind.Information, "executor", $"Mock install root: {executionRoot}", null),
                    .. EnsureMockRecipeExists(job.InstallScriptPath),
                    .. await ExecuteRecipeAsync(job.InstallScriptPath, executionRoot, cancellationToken)
                ],
            InstallScriptKind.PowerShell =>
                [
                    new JobLogRequest(LogLevelKind.Information, "executor", $"Install root: {executionRoot}", null),
                    .. await ExecutePowerShellAsync(job, executionRoot, mediaSession.Media, cancellationToken)
                ],
            _ => throw new InvalidOperationException($"Unsupported install script kind '{job.InstallScriptKind}'.")
        };

        await ReportAsync(
            reportEvent,
            5,
            JobState.Installing,
            $"Executing {job.InstallScriptKind} install script {job.InstallScriptPath}.",
            installLogs,
            cancellationToken);

        var detectionLogs = EvaluateDetectionRules(job, executionRoot);
        var detectionFailed = detectionLogs.Any(log => log.Level == LogLevelKind.Error);

        await ReportAsync(
            reportEvent,
            6,
            JobState.Validating,
            detectionFailed ? "Detection rule validation failed." : "Detection rules validated successfully.",
            detectionLogs,
            cancellationToken);

        if (detectionFailed)
        {
            throw new InvalidOperationException("One or more detection rules failed.");
        }

        var discoveredLaunchPath = DiscoverLaunchPathAfterInstall(job, executionRoot);

        cancellationToken.ThrowIfCancellationRequested();
        var completionLogs = new List<JobLogRequest>
        {
            new(LogLevelKind.Information, "agent", "Install recipe completed successfully.", null)
        };
        if (!string.IsNullOrWhiteSpace(discoveredLaunchPath))
        {
            completionLogs.Add(new(LogLevelKind.Information, "agent", $"Discovered launch path: {discoveredLaunchPath}", null));
        }

        await reportEvent(new JobEventRequest(7, JobState.Completed,
            $"Installation completed on machine {machineId}.",
            completionLogs,
            LaunchExecutablePath: discoveredLaunchPath,
            DetectedInstalled: true,
            ValidationMessage: "Install completed and detection rules passed.",
            InstallLocation: ResolveInstallLocation(job, executionRoot),
            ResolvedUninstallCommand: ResolveUninstallCommand(job, executionRoot)));
    }

    private static async Task ExecuteValidateAsync(
        Guid machineId,
        NextJobResponse job,
        Func<JobEventRequest, Task> reportEvent,
        CancellationToken cancellationToken)
    {
        var executionRoot = BuildExecutionRoot(machineId, job.PackageId);
        Directory.CreateDirectory(executionRoot);

        await ReportAsync(
            reportEvent,
            3,
            JobState.Preparing,
            $"Validating install state for {job.PackageName}.",
            new JobLogRequest(LogLevelKind.Information, "agent", $"Validation root: {executionRoot}", null),
            cancellationToken);

        var outcome = EvaluateInstallPresence(job, executionRoot);
        await ReportAsync(
            reportEvent,
            4,
            JobState.Validating,
            outcome.Installed ? "Install detection passed." : "Install detection reported the title is missing.",
            outcome.Logs,
            cancellationToken);

        await reportEvent(new JobEventRequest(
            5,
            JobState.Completed,
            outcome.Installed
                ? $"Validation completed. {job.PackageName} is installed on machine {machineId}."
                : $"Validation completed. {job.PackageName} is not installed on machine {machineId}.",
            [new JobLogRequest(LogLevelKind.Information, "agent", outcome.Summary, null)],
            LaunchExecutablePath: outcome.LaunchPath,
            DetectedInstalled: outcome.Installed,
            ValidationMessage: outcome.Summary,
            InstallLocation: outcome.InstallLocation,
            ResolvedUninstallCommand: outcome.UninstallCommand));
    }

    private static async Task ExecuteUninstallAsync(
        Guid machineId,
        NextJobResponse job,
        Func<JobEventRequest, Task> reportEvent,
        CancellationToken cancellationToken)
    {
        var executionRoot = BuildExecutionRoot(machineId, job.PackageId);
        Directory.CreateDirectory(executionRoot);

        var uninstallCommand = ResolveUninstallCommand(job, executionRoot);
        if (string.IsNullOrWhiteSpace(uninstallCommand))
        {
            throw new InvalidOperationException("No verified uninstall command is available for this title.");
        }

        var parsed = ParseCommand(uninstallCommand);
        await ReportAsync(
            reportEvent,
            3,
            JobState.Preparing,
            $"Resolved uninstall command for {job.PackageName}.",
            new JobLogRequest(LogLevelKind.Information, "agent", uninstallCommand, null),
            cancellationToken);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = parsed.FileName,
                Arguments = parsed.Arguments,
                UseShellExecute = false,
                WorkingDirectory = Directory.Exists(executionRoot) ? executionRoot : Environment.CurrentDirectory
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);

        await ReportAsync(
            reportEvent,
            4,
            JobState.Installing,
            $"Uninstall command exited with code {process.ExitCode}.",
            new JobLogRequest(LogLevelKind.Information, "executor", $"{parsed.FileName} {parsed.Arguments}".Trim(), null),
            cancellationToken);

        var outcome = EvaluateInstallPresence(job, executionRoot);
        await ReportAsync(
            reportEvent,
            5,
            JobState.Validating,
            outcome.Installed ? "Post-uninstall validation still detected the title." : "Post-uninstall validation confirms the title is gone.",
            outcome.Logs,
            cancellationToken);

        if (outcome.Installed)
        {
            throw new InvalidOperationException("Uninstall finished but validation still detects the title as installed.");
        }

        await reportEvent(new JobEventRequest(
            6,
            JobState.Completed,
            $"Uninstall completed on machine {machineId}.",
            [new JobLogRequest(LogLevelKind.Information, "agent", "Uninstall validation completed successfully.", null)],
            DetectedInstalled: false,
            ValidationMessage: outcome.Summary,
            ResolvedUninstallCommand: uninstallCommand));
    }

    private static string? DiscoverLaunchPathAfterInstall(NextJobResponse job, string executionRoot)
    {
        // 1. Check gamarr-launch-path.txt written by the install script
        var launchPathFile = Path.Combine(executionRoot, "installed", "gamarr-launch-path.txt");
        if (File.Exists(launchPathFile))
        {
            var path = File.ReadAllText(launchPathFile).Trim();
            if (IsEligibleInstalledPath(path))
            {
                return path;
            }
        }

        // 2. Registry-based discovery (searches uninstall entries by package name)
        return TryResolveInstalledLaunchPath(job, executionRoot);
    }

    private static IReadOnlyCollection<JobLogRequest> EnsureMockRecipeExists(string recipePath)
    {
        if (!File.Exists(recipePath))
        {
            throw new FileNotFoundException($"Mock recipe file was not found at '{recipePath}'.");
        }

        return [];
    }

    private static async Task ExecuteLaunchAsync(
        Guid machineId,
        NextJobResponse job,
        Func<JobEventRequest, Task> reportEvent,
        CancellationToken cancellationToken)
    {
        var executionRoot = BuildExecutionRoot(machineId, job.PackageId);
        var launchPath = ResolveLaunchPath(job, executionRoot);

        if (!File.Exists(launchPath))
        {
            throw new FileNotFoundException($"Launch target was not found at '{launchPath}'.");
        }

        await ReportAsync(
            reportEvent,
            3,
            JobState.Preparing,
            $"Preparing to launch {job.PackageName}.",
            [new JobLogRequest(LogLevelKind.Information, "launcher", $"Resolved launch path: {launchPath}", null)],
            cancellationToken);

        if (IsExecutableLaunchPath(launchPath))
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = launchPath,
                    WorkingDirectory = Path.GetDirectoryName(launchPath) ?? executionRoot,
                    UseShellExecute = true
                }
            };

            process.Start();
        }

        var message = IsExecutableLaunchPath(launchPath)
            ? $"Launch started for {job.PackageName}."
            : $"Simulated launch completed for mock artifact {launchPath}.";

        await ReportAsync(
            reportEvent,
            4,
            JobState.Completed,
            message,
            [new JobLogRequest(LogLevelKind.Information, "launcher", message, null)],
            cancellationToken);
    }

    private static async Task<IReadOnlyCollection<JobLogRequest>> ExecuteRecipeAsync(
        string recipePath,
        string executionRoot,
        CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(recipePath, cancellationToken);
        var logs = new List<JobLogRequest>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("LOG ", StringComparison.OrdinalIgnoreCase))
            {
                logs.Add(new JobLogRequest(LogLevelKind.Information, "recipe", line[4..].Trim(), null));
                continue;
            }

            if (line.StartsWith("WAIT ", StringComparison.OrdinalIgnoreCase))
            {
                var seconds = int.Parse(line[5..].Trim());
                logs.Add(new JobLogRequest(LogLevelKind.Information, "recipe", $"Waiting {seconds} second(s).", null));
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                continue;
            }

            if (line.StartsWith("WRITE_FILE ", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[11..];
                var separatorIndex = payload.IndexOf('|');
                if (separatorIndex <= 0)
                {
                    throw new InvalidOperationException("WRITE_FILE requires 'relativePath|content'.");
                }

                var relativePath = payload[..separatorIndex].Trim();
                var content = payload[(separatorIndex + 1)..];
                if (Path.IsPathRooted(relativePath))
                {
                    throw new InvalidOperationException("WRITE_FILE only supports relative paths.");
                }

                var destination = Path.Combine(executionRoot, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                await File.WriteAllTextAsync(destination, content, cancellationToken);
                logs.Add(new JobLogRequest(LogLevelKind.Information, "recipe", $"Wrote mock artifact {destination}.", null));
                continue;
            }

            throw new InvalidOperationException($"Unsupported mock recipe instruction '{line}'.");
        }

        return logs;
    }

    private static IReadOnlyCollection<JobLogRequest> EvaluateDetectionRules(NextJobResponse job, string executionRoot)
    {
        if (job.DetectionRules.Count == 0)
        {
            return [new JobLogRequest(LogLevelKind.Warning, "detection", "No detection rules were declared.", null)];
        }

        var logs = new List<JobLogRequest>();
        foreach (var rule in job.DetectionRules)
        {
            var result = EvaluateDetectionRule(rule, executionRoot);
            if (result is null)
            {
                logs.Add(new JobLogRequest(LogLevelKind.Warning, "detection", $"Unsupported detection rule '{rule.RuleType}'.", null));
                continue;
            }

            logs.Add(result);
        }

        return logs;
    }

    private static ValidationOutcome EvaluateInstallPresence(NextJobResponse job, string executionRoot)
    {
        var detectionLogs = EvaluateDetectionRules(job, executionRoot);
        var detectionFailed = detectionLogs.Any(log => log.Level == LogLevelKind.Error);

        var uninstallEntry = FindMatchingUninstallEntry(job);
        var installLocation = uninstallEntry?.InstallLocation;
        var uninstallCommand = ResolveUninstallCommand(job, executionRoot, uninstallEntry);
        var launchPath = TryResolveLaunchPathForValidation(job, executionRoot, uninstallEntry);

        if (job.DetectionRules.Count == 0)
        {
            var installed = !string.IsNullOrWhiteSpace(launchPath) ||
                            uninstallEntry is not null;
            var fallbackLogs = new List<JobLogRequest>
            {
                new(
                    installed ? LogLevelKind.Information : LogLevelKind.Warning,
                    "detection",
                    installed
                        ? "No detection rules were declared; fallback validation found install evidence."
                        : "No detection rules were declared and no fallback install evidence was found.",
                    null)
            };

            return new ValidationOutcome(
                installed,
                installed ? "Fallback validation found install evidence." : "Fallback validation could not find install evidence.",
                fallbackLogs,
                launchPath,
                installLocation,
                uninstallCommand);
        }

        if (!detectionFailed && !IsEligibleInstalledPath(launchPath))
        {
            return new ValidationOutcome(
                false,
                "Install detection did not produce a local launch target. Transient media paths do not count as installed.",
                [
                    .. detectionLogs,
                    new JobLogRequest(LogLevelKind.Warning, "detection", "No local launch executable could be resolved for this title.", null)
                ],
                null,
                installLocation,
                uninstallCommand);
        }

        if (!detectionFailed &&
            !HasMeaningfulInstalledEvidence(job, executionRoot, launchPath, uninstallEntry) &&
            DetectionRulesDependOnlyOnTransientEvidence(job, executionRoot))
        {
            return new ValidationOutcome(
                false,
                "Detection passed only on transient Gamarr/media evidence; no local installed files were found.",
                [
                    .. detectionLogs,
                    new JobLogRequest(LogLevelKind.Warning, "detection", "Transient evidence such as a Gamarr marker or mounted-media path is not enough to count as installed.", null)
                ],
                null,
                installLocation,
                uninstallCommand);
        }

        return new ValidationOutcome(
            !detectionFailed,
            detectionFailed ? "One or more detection rules failed." : "Detection rules passed.",
            detectionLogs,
            launchPath,
            installLocation,
            uninstallCommand);
    }

    private static bool HasMeaningfulInstalledEvidence(
        NextJobResponse job,
        string executionRoot,
        string? launchPath,
        UninstallEntryDetails? uninstallEntry)
    {
        if (IsEligibleInstalledPath(launchPath))
        {
            return true;
        }

        if (uninstallEntry is not null)
        {
            return true;
        }

        return job.DetectionRules.Any(rule =>
        {
            if (!string.Equals(rule.RuleType, "FileExists", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = ExpandKnownPlaceholders(rule.Value, executionRoot, executionRoot, []);
            return IsEligibleInstalledPath(path);
        });
    }

    private static bool DetectionRulesDependOnlyOnTransientEvidence(NextJobResponse job, string executionRoot)
    {
        if (job.DetectionRules.Count == 0)
        {
            return true;
        }

        foreach (var rule in job.DetectionRules)
        {
            if (string.Equals(rule.RuleType, "UninstallEntryExists", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.RuleType, "RegistryValueExists", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.RuleType, "FileVersionEquals", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.RuleType, "FileVersionAtLeast", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(rule.RuleType, "FileExists", StringComparison.OrdinalIgnoreCase))
            {
                var path = ExpandKnownPlaceholders(rule.Value, executionRoot, executionRoot, []);
                if (IsEligibleInstalledPath(path))
                {
                    return false;
                }

                if (!rule.Value.Contains("gamarr-library-import.marker", StringComparison.OrdinalIgnoreCase) &&
                    !IsMountedMediaPath(path))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static JobLogRequest? EvaluateDetectionRule(DetectionRuleResponse rule, string executionRoot)
    {
        if (string.Equals(rule.RuleType, "FileExists", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExpandKnownPlaceholders(rule.Value, executionRoot, executionRoot, []);
            return File.Exists(path)
                ? new JobLogRequest(LogLevelKind.Information, "detection", $"Detection rule passed for {path}.", null)
                : new JobLogRequest(LogLevelKind.Error, "detection", $"Detection rule failed. Missing file {path}.", null);
        }

        if (string.Equals(rule.RuleType, "RegistryValueExists", StringComparison.OrdinalIgnoreCase))
        {
            var parts = rule.Value.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return new JobLogRequest(LogLevelKind.Error, "detection", $"RegistryValueExists requires 'KeyPath|ValueName'. Received '{rule.Value}'.", null);
            }

            try
            {
                var key = OpenRegistryKey(parts[0]);
                var value = key?.GetValue(parts[1]);
                return value is not null
                    ? new JobLogRequest(LogLevelKind.Information, "detection", $"Registry value '{parts[1]}' exists under '{parts[0]}'.", null)
                    : new JobLogRequest(LogLevelKind.Error, "detection", $"Registry value '{parts[1]}' was not found under '{parts[0]}'.", null);
            }
            catch (Exception ex)
            {
                return new JobLogRequest(LogLevelKind.Error, "detection", $"Registry detection failed for '{rule.Value}': {ex.Message}", null);
            }
        }

        if (string.Equals(rule.RuleType, "UninstallEntryExists", StringComparison.OrdinalIgnoreCase))
        {
            var target = rule.Value.Trim();
            var found = FindUninstallEntry(target);
            return found
                ? new JobLogRequest(LogLevelKind.Information, "detection", $"Uninstall entry matching '{target}' was found.", null)
                : new JobLogRequest(LogLevelKind.Error, "detection", $"No uninstall entry matching '{target}' was found.", null);
        }

        if (string.Equals(rule.RuleType, "FileVersionEquals", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rule.RuleType, "FileVersionAtLeast", StringComparison.OrdinalIgnoreCase))
        {
            var parts = rule.Value.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return new JobLogRequest(LogLevelKind.Error, "detection", $"{rule.RuleType} requires 'Path|Version'. Received '{rule.Value}'.", null);
            }

            var path = ExpandKnownPlaceholders(parts[0], executionRoot, executionRoot, []);
            if (!File.Exists(path))
            {
                return new JobLogRequest(LogLevelKind.Error, "detection", $"Version detection failed. Missing file {path}.", null);
            }

            try
            {
                var actualVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (!Version.TryParse(actualVersion, out var actual) || !Version.TryParse(parts[1], out var expected))
                {
                    return new JobLogRequest(LogLevelKind.Error, "detection", $"Version detection failed. Could not parse '{actualVersion}' or '{parts[1]}'.", null);
                }

                var passed = string.Equals(rule.RuleType, "FileVersionEquals", StringComparison.OrdinalIgnoreCase)
                    ? actual == expected
                    : actual >= expected;

                return passed
                    ? new JobLogRequest(LogLevelKind.Information, "detection", $"Version rule passed for {path}: {actual}.", null)
                    : new JobLogRequest(LogLevelKind.Error, "detection", $"Version rule failed for {path}: expected {rule.RuleType.Replace("FileVersion", string.Empty)} {expected}, actual {actual}.", null);
            }
            catch (Exception ex)
            {
                return new JobLogRequest(LogLevelKind.Error, "detection", $"Version detection failed for '{path}': {ex.Message}", null);
            }
        }

        return null;
    }

    private static async Task<PreparedMediaSession> PrepareMediaAsync(NextJobResponse job, string executionRoot, CancellationToken cancellationToken)
    {
        var prepared = new List<PreparedMedia>();
        var cleanup = new List<Func<CancellationToken, Task>>();

        var orderedMedia = CollapseDescriptorBackedMediaPairs(job.Media)
            .OrderBy(media => media.DiscNumber ?? int.MaxValue)
            .ThenBy(media => media.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < orderedMedia.Length; index++)
        {
            var media = orderedMedia[index];
            var sourcePath = ExpandKnownPlaceholders(media.Path, executionRoot, executionRoot, prepared);
            if (Directory.Exists(sourcePath))
            {
                prepared.Add(new PreparedMedia(media.Label, media.MediaType, sourcePath, sourcePath, media.DiscNumber, media.EntrypointHint, media.SourceKind, media.ScratchPolicy, "DirectFolder"));
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Media source was not found at '{sourcePath}'.");
            }

            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            var sourceKind = string.IsNullOrWhiteSpace(media.SourceKind) ? "Auto" : media.SourceKind;
            var scratchPolicy = NormalizeScratchPolicy(media.ScratchPolicy);

            if (IsRawSectorImage(extension))
            {
                var winCDEmu = FindWinCDEmuExecutable();
                if (winCDEmu is not null)
                {
                    // Preferred: mount natively with WinCDEmu (no conversion needed).
                    var mountedRoot = await MountWithWinCDEmuAsync(sourcePath, winCDEmu, cancellationToken);
                    cleanup.Add(token => DismountWithWinCDEmuAsync(sourcePath, winCDEmu, token));
                    prepared.Add(new PreparedMedia(media.Label, media.MediaType, sourcePath, mountedRoot, media.DiscNumber, media.EntrypointHint, media.SourceKind, media.ScratchPolicy, "MountedVolume"));
                }
                else
                {
                    // Fallback: convert to ISO on the fly and mount with Mount-DiskImage.
                    var scratchRoot = BuildJobScratchRoot(executionRoot, job.Id);
                    var tempIso = await ConvertRawSectorImageToIsoAsync(sourcePath, scratchRoot, cancellationToken);
                    cleanup.Add(_ =>
                    {
                        return SafeDeleteDiskImageFileAsync(tempIso, CancellationToken.None);
                    });
                    var mountedRoot = await MountDiskImageAsync(tempIso, ".iso", cancellationToken);
                    cleanup.Add(token => DismountDiskImageAsync(tempIso, token));
                    prepared.Add(new PreparedMedia(media.Label, media.MediaType, sourcePath, mountedRoot, media.DiscNumber, media.EntrypointHint, media.SourceKind, media.ScratchPolicy, "MountedVolume"));
                }
                continue;
            }

            if (ShouldMountSource(sourceKind, extension))
            {
                try
                {
                    var mountedRoot = await MountDiskImageAsync(sourcePath, extension, cancellationToken);
                    cleanup.Add(token => DismountDiskImageAsync(sourcePath, token));
                    prepared.Add(new PreparedMedia(media.Label, media.MediaType, sourcePath, mountedRoot, media.DiscNumber, media.EntrypointHint, media.SourceKind, media.ScratchPolicy, "MountedVolume"));
                    continue;
                }
                catch when (CanFallbackToExtraction(extension))
                {
                }
            }

            if (ShouldExtractSource(sourceKind, extension))
            {
                var extractRoot = scratchPolicy == "Persistent"
                    ? Path.Combine(GamarrRoot, "MediaCache", ComputeStablePathToken(sourcePath))
                    : Path.Combine(executionRoot, "_media", index.ToString("00"));

                Directory.CreateDirectory(extractRoot);
                await ExtractDiskImageAsync(ResolveExtractionSourcePath(sourcePath), extractRoot, cancellationToken);
                if (scratchPolicy != "Persistent")
                {
                    cleanup.Add(_ =>
                    {
                        if (Directory.Exists(extractRoot))
                        {
                            Directory.Delete(extractRoot, recursive: true);
                        }

                        return Task.CompletedTask;
                    });
                }

                prepared.Add(new PreparedMedia(media.Label, media.MediaType, sourcePath, extractRoot, media.DiscNumber, media.EntrypointHint, media.SourceKind, media.ScratchPolicy, scratchPolicy == "Persistent" ? "PersistentCache" : "ExtractedWorkspace"));
                continue;
            }

            prepared.Add(new PreparedMedia(media.Label, media.MediaType, sourcePath, Path.GetDirectoryName(sourcePath) ?? sourcePath, media.DiscNumber, media.EntrypointHint, media.SourceKind, media.ScratchPolicy, "DirectFile"));
        }

        return new PreparedMediaSession(prepared, cleanup);
    }

    private static async Task<IReadOnlyCollection<JobLogRequest>> ExecutePowerShellAsync(
        NextJobResponse job,
        string executionRoot,
        IReadOnlyCollection<PreparedMedia> media,
        CancellationToken cancellationToken)
    {
        var (scriptPath, cleanup) = await ResolvePowerShellScriptAsync(job, executionRoot, media, cancellationToken);

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? executionRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            ApplyExecutionEnvironment(process.StartInfo, job, executionRoot, media);

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var logs = new List<JobLogRequest>
            {
                new(LogLevelKind.Information, "powershell", $"PowerShell exited with code {process.ExitCode}.", null)
            };

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                logs.Add(new JobLogRequest(LogLevelKind.Information, "powershell.stdout", stdout.Trim(), null));
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                logs.Add(new JobLogRequest(LogLevelKind.Warning, "powershell.stderr", stderr.Trim(), null));
            }

            if (process.ExitCode != 0)
            {
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    details.Add($"stdout: {stdout.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    details.Add($"stderr: {stderr.Trim()}");
                }

                var suffix = details.Count > 0 ? $" {string.Join(" ", details)}" : string.Empty;
                throw new InvalidOperationException($"PowerShell install script exited with code {process.ExitCode}.{suffix}");
            }

            return logs;
        }
        finally
        {
            cleanup?.Invoke();
        }
    }

    private static IReadOnlyCollection<PackageMediaResponse> CollapseDescriptorBackedMediaPairs(IReadOnlyCollection<PackageMediaResponse> media)
    {
        if (media.Count <= 1)
        {
            return media;
        }

        var descriptorKeys = media
            .Where(item => IsDescriptorImagePath(item.Path))
            .Select(item => BuildMediaPairKey(item.Path))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (descriptorKeys.Count == 0)
        {
            return media;
        }

        return media
            .Where(item => !IsCompanionOnlyImagePath(item.Path) ||
                           !descriptorKeys.Contains(BuildMediaPairKey(item.Path)))
            .ToArray();
    }

    private static bool IsDescriptorImagePath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".cue" or ".mds" or ".ccd";

    private static bool IsCompanionOnlyImagePath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".bin" or ".mdf" or ".img";

    private static string BuildMediaPairKey(string path) =>
        $"{Path.GetDirectoryName(path) ?? string.Empty}|{Path.GetFileNameWithoutExtension(path)}";

    private static void ApplyExecutionEnvironment(ProcessStartInfo startInfo, NextJobResponse job, string executionRoot, IReadOnlyCollection<PreparedMedia> media)
    {
        startInfo.Environment["GAMARR_INSTALL_ROOT"] = executionRoot;
        startInfo.Environment["GAMARR_PACKAGE_NAME"] = job.PackageName;
        startInfo.Environment["MOCK_INSTALL_ROOT"] = executionRoot;
        startInfo.Environment["GAMARR_PAYLOAD_ROOT"] = Path.Combine(executionRoot, "payload");
        startInfo.Environment["GAMARR_INSTALLED_ROOT"] = Path.Combine(executionRoot, "installed");
        startInfo.Environment["GAMARR_MEDIA_COUNT"] = media.Count.ToString();
        startInfo.Environment["GAMARR_INSTALL_STRATEGY"] = job.InstallStrategy;
        startInfo.Environment["GAMARR_INSTALLER_FAMILY"] = job.InstallerFamily;
        startInfo.Environment["GAMARR_INSTALLER_PATH"] = job.InstallerPath ?? string.Empty;
        startInfo.Environment["GAMARR_INSTALLER_ARGS"] = job.SilentArguments ?? string.Empty;
        startInfo.Environment["GAMARR_INSTALL_DIAGNOSTICS"] = job.InstallDiagnostics;

        if (media.FirstOrDefault() is { } primaryMedia)
        {
            startInfo.Environment["GAMARR_PRIMARY_MEDIA_ROOT"] = primaryMedia.RootPath;
        }

        foreach (var item in media.Select((value, index) => (value, index)))
        {
            startInfo.Environment[$"GAMARR_MEDIA_{item.index}_ROOT"] = item.value.RootPath;
            startInfo.Environment[$"GAMARR_MEDIA_{item.index}_PATH"] = item.value.SourcePath;
            startInfo.Environment[$"GAMARR_MEDIA_{item.index}_TYPE"] = item.value.MediaType;
            startInfo.Environment[$"GAMARR_MEDIA_{item.index}_SOURCE_KIND"] = item.value.SourceKind;
            startInfo.Environment[$"GAMARR_MEDIA_{item.index}_SCRATCH_POLICY"] = item.value.ScratchPolicy;

            if (item.value.DiscNumber is { } discNumber)
            {
                startInfo.Environment[$"GAMARR_MEDIA_{item.index}_DISC"] = discNumber.ToString();
            }

            if (!string.IsNullOrWhiteSpace(item.value.EntrypointHint))
            {
                startInfo.Environment[$"GAMARR_MEDIA_{item.index}_ENTRYPOINT"] = item.value.EntrypointHint!;
            }
        }
    }

    private static string ExpandPlaceholders(string value, string executionRoot) =>
        ExpandKnownPlaceholders(value, executionRoot, executionRoot, []);

    private static string ExpandKnownPlaceholders(
        string value,
        string executionRoot,
        string primaryMediaRoot,
        IReadOnlyCollection<PreparedMedia> media)
    {
        var expanded = value
            .Replace("%MOCK_INSTALL_ROOT%", executionRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%INSTALL_ROOT%", executionRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%PRIMARY_MEDIA_ROOT%", primaryMediaRoot, StringComparison.OrdinalIgnoreCase);

        foreach (var item in media.Select((entry, index) => (entry, index)))
        {
            expanded = expanded.Replace($"%GAMARR_MEDIA_{item.index}_ROOT%", item.entry.RootPath, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(item.entry.EntrypointHint))
            {
                expanded = expanded.Replace($"%GAMARR_MEDIA_{item.index}_ENTRYPOINT%", item.entry.EntrypointHint, StringComparison.OrdinalIgnoreCase);
            }
        }

        return expanded;
    }

    private static string BuildExecutionRoot(Guid machineId, Guid packageId) =>
        Path.Combine(MockInstallRoot, machineId.ToString("N"), packageId.ToString("N"));

    private static string BuildJobScratchRoot(string executionRoot, Guid jobId) =>
        Path.Combine(executionRoot, "_job-scratch", jobId.ToString("N"));

    private static string ResolveLaunchPath(NextJobResponse job, string executionRoot)
    {
        if (!string.IsNullOrWhiteSpace(job.LaunchExecutablePath))
        {
            var persistedPath = ExpandKnownPlaceholders(job.LaunchExecutablePath, executionRoot, executionRoot, []);
            if (IsEligibleInstalledPath(persistedPath))
            {
                return persistedPath;
            }
        }

        var discoveredLaunchPath = Path.Combine(executionRoot, "installed", "gamarr-launch-path.txt");
        if (File.Exists(discoveredLaunchPath))
        {
            var launchPath = File.ReadAllText(discoveredLaunchPath).Trim();
            if (IsEligibleInstalledPath(launchPath))
            {
                return launchPath;
            }
        }

        var installedLaunchPath = TryResolveInstalledLaunchPath(job, executionRoot);
        if (!string.IsNullOrWhiteSpace(installedLaunchPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(discoveredLaunchPath)!);
            File.WriteAllText(discoveredLaunchPath, installedLaunchPath);
            return installedLaunchPath;
        }

        var detectionPath = job.DetectionRules
            .FirstOrDefault(rule =>
                string.Equals(rule.RuleType, "FileExists", StringComparison.OrdinalIgnoreCase) &&
                !rule.Value.Contains("gamarr-library-import.marker", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrWhiteSpace(detectionPath))
        {
            throw new InvalidOperationException("No launch target or file-based detection rule is available for this package.");
        }

        var candidate = ExpandPlaceholders(detectionPath, executionRoot);
        if (IsEligibleInstalledPath(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException($"Launch target was not found at '{candidate}'.");
    }

    private static string? TryResolveInstalledLaunchPath(NextJobResponse job, string executionRoot)
    {
        var uninstallEntry = FindMatchingUninstallEntry(job);
        if (uninstallEntry is not null)
        {
            var directDisplayIcon = TryResolveDisplayIconExecutable(uninstallEntry.DisplayIcon);
            if (!string.IsNullOrWhiteSpace(directDisplayIcon))
            {
                return directDisplayIcon;
            }

            var candidateRoots = new[]
            {
                uninstallEntry.InstallLocation,
                ExtractCommandDirectory(uninstallEntry.DisplayIcon),
                ExtractCommandDirectory(uninstallEntry.UninstallString),
                ExtractCommandDirectory(uninstallEntry.QuietUninstallString)
            }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            foreach (var candidateRoot in candidateRoots)
            {
                var winner = FindBestLaunchExecutable(candidateRoot!, job.PackageName, executionRoot);
                if (!string.IsNullOrWhiteSpace(winner))
                {
                    return winner;
                }
            }
        }

        return null;
    }

    private static string? TryResolveLaunchPathForValidation(NextJobResponse job, string executionRoot, UninstallEntryDetails? uninstallEntry)
    {
        if (!string.IsNullOrWhiteSpace(job.LaunchExecutablePath))
        {
            var declared = ExpandKnownPlaceholders(job.LaunchExecutablePath, executionRoot, executionRoot, []);
            if (IsEligibleInstalledPath(declared))
            {
                return declared;
            }
        }

        if (uninstallEntry is not null)
        {
            var directDisplayIcon = TryResolveDisplayIconExecutable(uninstallEntry.DisplayIcon);
            if (!string.IsNullOrWhiteSpace(directDisplayIcon))
            {
                return directDisplayIcon;
            }
        }

        return TryResolveInstalledLaunchPath(job, executionRoot);
    }

    private static UninstallEntryDetails? FindMatchingUninstallEntry(NextJobResponse job)
    {
        var searchTerms = job.DetectionRules
            .Where(rule => string.Equals(rule.RuleType, "UninstallEntryExists", StringComparison.OrdinalIgnoreCase))
            .Select(rule => rule.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Append(job.PackageName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var searchTerm in searchTerms)
        {
            var uninstallEntry = FindUninstallEntryDetails(searchTerm);
            if (uninstallEntry is not null)
            {
                return uninstallEntry;
            }
        }

        return null;
    }

    private static string? ResolveInstallLocation(NextJobResponse job, string executionRoot)
    {
        var uninstallEntry = FindMatchingUninstallEntry(job);
        if (!string.IsNullOrWhiteSpace(uninstallEntry?.InstallLocation))
        {
            return uninstallEntry.InstallLocation;
        }

        return Directory.Exists(executionRoot) ? executionRoot : null;
    }

    private static string? ResolveUninstallCommand(NextJobResponse job, string executionRoot, UninstallEntryDetails? uninstallEntry = null)
    {
        if (!string.IsNullOrWhiteSpace(job.UninstallScriptPath))
        {
            var resolvedPath = job.UninstallScriptPath!;
            if (!Path.IsPathRooted(resolvedPath))
            {
                var installLocation = uninstallEntry?.InstallLocation ?? ResolveInstallLocation(job, executionRoot);
                if (!string.IsNullOrWhiteSpace(installLocation))
                {
                    resolvedPath = Path.Combine(installLocation!, resolvedPath);
                }
                else
                {
                    resolvedPath = ExpandKnownPlaceholders(resolvedPath, executionRoot, executionRoot, []);
                }
            }

            var arguments = string.IsNullOrWhiteSpace(job.UninstallArguments) ? string.Empty : $" {job.UninstallArguments.Trim()}";
            return QuoteCommandPath(resolvedPath) + arguments;
        }

        uninstallEntry ??= FindMatchingUninstallEntry(job);
        if (uninstallEntry is null)
        {
            return null;
        }

        var command = !string.IsNullOrWhiteSpace(uninstallEntry.QuietUninstallString)
            ? uninstallEntry.QuietUninstallString
            : uninstallEntry.UninstallString;

        return NormalizeUninstallCommand(command);
    }

    private static string QuoteCommandPath(string path) =>
        path.Contains(' ') && !path.StartsWith('"') ? $"\"{path}\"" : path;

    private static bool IsEligibleInstalledPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return !IsMountedMediaPath(path);
    }

    private static bool IsMountedMediaPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.CDRom;
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeUninstallCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var parsed = ParseCommand(command.Trim());
        if (parsed.FileName.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parsed.FileName, "msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var args = parsed.Arguments
                .Replace("/I", "/X", StringComparison.OrdinalIgnoreCase)
                .Replace(" /i ", " /x ", StringComparison.OrdinalIgnoreCase);
            return $"{parsed.FileName} {args}".Trim();
        }

        return $"{parsed.FileName} {parsed.Arguments}".Trim();
    }

    private static (string FileName, string Arguments) ParseCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 0)
            {
                var fileName = trimmed[1..closingQuote];
                var arguments = trimmed[(closingQuote + 1)..].Trim();
                return (fileName, arguments);
            }
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            var fileName = trimmed[..(exeIndex + 4)].Trim();
            var arguments = trimmed[(exeIndex + 4)..].Trim();
            return (fileName, arguments);
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace < 0
            ? (trimmed, string.Empty)
            : (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].Trim());
    }

    private static string? FindBestLaunchExecutable(string rootPath, string packageName, string executionRoot)
    {
        IEnumerable<string> executables;
        try
        {
            executables = Directory.EnumerateFiles(rootPath, "*.exe", SearchOption.AllDirectories);
        }
        catch
        {
            return null;
        }

        var titleTokens = packageName
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Select(token => token.ToLowerInvariant())
            .Distinct()
            .ToArray();

        var winner = executables
            .Where(path => !IsIgnoredLaunchExecutable(path))
            .Select(path => new
            {
                Path = path,
                Score = ScoreLaunchExecutable(path, titleTokens, executionRoot)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return winner?.Path;
    }

    private static bool IsIgnoredLaunchExecutable(string path)
    {
        var fileName = Path.GetFileName(path);
        var normalizedPath = path.ToLowerInvariant();

        return fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("setup", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("install", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("autorun", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("config", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains(@"\redist\") ||
               normalizedPath.Contains(@"\redistributable\") ||
               normalizedPath.Contains(@"\support\") ||
               normalizedPath.Contains(@"\directx\") ||
               normalizedPath.Contains(@"\docs\") ||
               normalizedPath.Contains(@"\manual");
    }

    private static int ScoreLaunchExecutable(string path, IReadOnlyCollection<string> titleTokens, string executionRoot)
    {
        var score = 0;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var normalizedPath = path.ToLowerInvariant();
        var normalizedName = baseName.ToLowerInvariant();

        foreach (var token in titleTokens)
        {
            if (normalizedName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 35;
            }
            else if (normalizedPath.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }

        if (!normalizedPath.StartsWith(executionRoot, StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length >= 50 * 1024 * 1024)
            {
                score += 30;
            }
            else if (info.Length >= 10 * 1024 * 1024)
            {
                score += 20;
            }
            else if (info.Length >= 1 * 1024 * 1024)
            {
                score += 10;
            }
        }
        catch
        {
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(path);
            var signature = string.Join(' ', new[] { version.FileDescription, version.ProductName }.Where(value => !string.IsNullOrWhiteSpace(value)))
                .ToLowerInvariant();
            foreach (var token in titleTokens)
            {
                if (signature.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    score += 20;
                }
            }

            if (!string.IsNullOrWhiteSpace(signature))
            {
                score += 5;
            }
        }
        catch
        {
        }

        return score;
    }

    private static string? TryResolveDisplayIconExecutable(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var commandPath = ExtractCommandPath(displayIcon);
        if (string.IsNullOrWhiteSpace(commandPath) || !File.Exists(commandPath) || IsIgnoredLaunchExecutable(commandPath))
        {
            return null;
        }

        return commandPath;
    }

    private static string? ExtractCommandDirectory(string? command)
    {
        var path = ExtractCommandPath(command);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return path;
        }

        return File.Exists(path) ? Path.GetDirectoryName(path) : null;
    }

    private static string? ExtractCommandPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var value = command.Trim();
        var commaIndex = value.IndexOf(',');
        if (commaIndex > 0)
        {
            value = value[..commaIndex];
        }

        if (value.StartsWith('"'))
        {
            var closingQuote = value.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                return value[1..closingQuote];
            }
        }

        var exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0)
        {
            return value[..(exeIndex + 4)].Trim();
        }

        return value;
    }

    internal static string? FindWinCDEmuExecutable()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\WinCDEmu\batchmnt.exe",
            @"C:\Program Files\WinCDEmu\batchmnt.exe"
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    internal static async Task<string> MountStandaloneImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var mountPath = ResolveStandaloneMountPath(imagePath);
        var extension = Path.GetExtension(mountPath).ToLowerInvariant();

        if (IsNativeMountableImage(extension))
        {
            return await MountDiskImageAsync(mountPath, extension, cancellationToken);
        }

        if (RequiresWinCDEmuMount(extension))
        {
            var winCDEmu = FindWinCDEmuExecutable();
            if (winCDEmu is null)
            {
                throw new InvalidOperationException(
                    $"Image '{Path.GetFileName(mountPath)}' requires WinCDEmu for manual mounting on this machine.");
            }

            return await MountWithWinCDEmuAsync(mountPath, winCDEmu, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Unsupported image type '{extension}'. Supported manual mount types are ISO, IMG, VHD, VHDX, MDF/MDS, BIN/CUE, NRG, CCD, and CDI.");
    }

    internal static async Task DismountStandaloneImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var mountPath = ResolveStandaloneMountPath(imagePath);
        var extension = Path.GetExtension(mountPath).ToLowerInvariant();

        if (IsNativeMountableImage(extension))
        {
            await DismountDiskImageAsync(mountPath, cancellationToken);
            return;
        }

        if (RequiresWinCDEmuMount(extension))
        {
            var winCDEmu = FindWinCDEmuExecutable();
            if (winCDEmu is null)
            {
                throw new InvalidOperationException(
                    $"Image '{Path.GetFileName(mountPath)}' was mounted via WinCDEmu semantics, but WinCDEmu is not available to dismount it.");
            }

            await DismountWithWinCDEmuAsync(mountPath, winCDEmu, cancellationToken);
            return;
        }

        throw new InvalidOperationException($"Unsupported image type '{extension}' for dismount.");
    }

    private static async Task<string> MountWithWinCDEmuAsync(string imagePath, string batchmntPath, CancellationToken cancellationToken)
    {
        // Snapshot CD-ROM drives before mounting so we can detect the new one.
        var before = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.CDRom)
            .Select(d => d.RootDirectory.FullName[0])
            .ToHashSet();

        var psi = new ProcessStartInfo(batchmntPath, $"\"{imagePath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start batchmnt.exe.");
        await proc.WaitForExitAsync(cancellationToken);

        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"WinCDEmu batchmnt.exe failed (exit {proc.ExitCode}): {stderr.Trim()}");
        }

        // Give Windows a moment to assign a drive letter.
        await Task.Delay(1200, cancellationToken);

        var after = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.CDRom)
            .Select(d => d.RootDirectory.FullName[0])
            .ToHashSet();

        var newDrive = after.Except(before).FirstOrDefault();
        if (newDrive == default)
        {
            throw new InvalidOperationException($"WinCDEmu mounted '{Path.GetFileName(imagePath)}' but no new drive letter appeared.");
        }

        return $"{newDrive}:\\";
    }

    private static async Task DismountWithWinCDEmuAsync(string imagePath, string batchmntPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(batchmntPath, $"\"{imagePath}\" /u")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        if (proc is not null)
        {
            await proc.WaitForExitAsync(cancellationToken);
        }
    }

    private static bool IsNativeMountableImage(string extension) =>
        extension is ".iso" or ".img" or ".vhd" or ".vhdx";

    private static bool RequiresWinCDEmuMount(string extension) =>
        extension is ".mdf" or ".mds" or ".bin" or ".cue" or ".nrg" or ".ccd" or ".cdi";

    private static bool IsExtractableDiskImage(string extension) =>
        extension is ".nrg" or ".ccd" or ".cdi";

    // Raw sector disc images that can be converted to ISO on the fly
    private static bool IsRawSectorImage(string extension) =>
        extension is ".mdf" or ".mds" or ".bin" or ".cue";

    private static bool CanFallbackToExtraction(string extension) =>
        extension is ".img";

    private static async Task<string> ConvertRawSectorImageToIsoAsync(string sourceRootPath, string scratchRoot, CancellationToken cancellationToken)
    {
        // For descriptor sidecar files, use the actual data file
        var dataPath = Path.GetExtension(sourceRootPath).ToLowerInvariant() switch
        {
            ".mds" => ResolveCompanionFile(Path.GetDirectoryName(sourceRootPath) ?? "", Path.GetFileNameWithoutExtension(sourceRootPath), ".mdf", sourceRootPath),
            ".cue" => ResolveCompanionFile(Path.GetDirectoryName(sourceRootPath) ?? "", Path.GetFileNameWithoutExtension(sourceRootPath), ".bin", sourceRootPath),
            _ => sourceRootPath
        };

        Directory.CreateDirectory(scratchRoot);
        var tempIso = Path.Combine(scratchRoot, $"{Path.GetFileNameWithoutExtension(dataPath)}-{Guid.NewGuid():N}.iso");

        await Task.Run(() =>
        {
            using var input = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            using var output = new FileStream(tempIso, FileMode.Create, FileAccess.Write, FileShare.None, 65536);

            // Read enough to probe for the ISO 9660 PVD at known sector offsets
            var probeSize = Math.Min(40960L, input.Length);
            var probe = new byte[probeSize];
            var read = input.Read(probe, 0, (int)probeSize);
            input.Seek(0, SeekOrigin.Begin);

            var (sectorSize, dataOffset) = DetectDiscSectorLayout(probe, read);

            if (sectorSize == 0)
            {
                output.Dispose();
                throw new InvalidOperationException(
                    $"Cannot determine sector layout of '{Path.GetFileName(dataPath)}' — " +
                    "the ISO 9660 Primary Volume Descriptor was not found at any known sector offset. " +
                    "Install WinCDEmu to mount this disc image natively without conversion.");
            }

            if (sectorSize == 2048)
            {
                // Already plain 2048-byte sectors — copy as-is.
                input.CopyTo(output);
            }
            else
            {
                // Strip per-sector overhead to produce plain 2048-byte/sector ISO data.
                const int dataSize = 2048;
                var sectorCount = input.Length / sectorSize;
                var buf = new byte[sectorSize];
                for (long i = 0; i < sectorCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var n = input.Read(buf, 0, sectorSize);
                    if (n < sectorSize) break;
                    output.Write(buf, dataOffset, dataSize);
                }
            }
        }, cancellationToken);

        return tempIso;
    }

    /// <summary>
    /// Detects disc sector layout by locating the ISO 9660 Primary Volume Descriptor.
    /// Returns (sectorSize, dataOffset) or (0, 0) if unknown.
    /// </summary>
    private static (int sectorSize, int dataOffset) DetectDiscSectorLayout(byte[] probe, int probeLen)
    {
        // ISO 9660 PVD is at logical sector 16. Check well-known physical offsets:
        //   sigOffset  = sectorSize * 16 + dataOffset + 1  (the +1 skips the descriptor type byte before "CD001")
        // 2048-byte plain ISO:       16*2048 + 0 + 1 = 32769
        // 2352-byte Mode 1:          16*2352 + 16 + 1 = 37649   (sync=12, header=4 → dataOffset=16)
        // 2352-byte Mode 2 XA:       16*2352 + 24 + 1 = 37657   (sync=12, header=4, subheader=8 → dataOffset=24)
        // 2336-byte Mode 2 raw:      16*2336 + 8  + 1 = 37385   (subheader=8 only, no sync/header)
        ReadOnlySpan<(int sigOffset, int sectorSize, int dataOffset)> candidates =
        [
            (32769,  2048, 0),
            (37649,  2352, 16),
            (37657,  2352, 24),
            (37385,  2336, 8),
        ];

        foreach (var (sigOffset, sectorSize, dataOffset) in candidates)
        {
            if (sigOffset + 4 < probeLen &&
                probe[sigOffset]     == 'C' &&
                probe[sigOffset + 1] == 'D' &&
                probe[sigOffset + 2] == '0' &&
                probe[sigOffset + 3] == '0' &&
                probe[sigOffset + 4] == '1')
            {
                return (sectorSize, dataOffset);
            }
        }

        return (0, 0);
    }

    private static Task<string> MountDiskImageAsync(string imagePath, string extension, CancellationToken cancellationToken) =>
        extension is ".vhd" or ".vhdx"
            ? AttachVirtualDiskAsync(imagePath, cancellationToken)
            : MountOpticalImageAsync(imagePath, cancellationToken);

    internal static async Task<string> MountOpticalImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var script = $$"""
$mount = Get-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}' -ErrorAction SilentlyContinue
if (-not $mount -or -not $mount.Attached) {
  $mount = Mount-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}' -PassThru
  Start-Sleep -Milliseconds 750
  $mount = Get-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}' -ErrorAction SilentlyContinue
}
$volumes = $mount | Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter }
if (-not $volumes) { throw 'No mounted volume with drive letter was found.' }
$volumes | Select-Object -ExpandProperty DriveLetter | ConvertTo-Json -Compress
""";

        var output = await ExecutePowerShellCommandAsync(script, cancellationToken);
        return ToDriveRoot(imagePath, output);
    }

    private static async Task<string> AttachVirtualDiskAsync(string imagePath, CancellationToken cancellationToken)
    {
        var script = $$"""
$mount = Get-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}' -ErrorAction SilentlyContinue
if (-not $mount -or -not $mount.Attached) {
  $mount = Mount-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}' -PassThru
  Start-Sleep -Milliseconds 750
  $mount = Get-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}' -ErrorAction SilentlyContinue
}
if (-not $mount.Number) { throw 'Mounted virtual disk has no disk number.' }
$disk = Get-Disk -Number $mount.Number -ErrorAction Stop
$disk | Set-Disk -IsOffline $false -IsReadOnly $false -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
$partition = Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue | Where-Object { $_.Type -ne 'Reserved' } | Select-Object -First 1
if (-not $partition) { throw 'Mounted virtual disk has no usable partition.' }
if (-not $partition.DriveLetter) {
  $partition | Add-PartitionAccessPath -AssignDriveLetter -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  $partition = Get-Partition -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -ErrorAction SilentlyContinue
}
$driveRoot = $null
if ($partition.DriveLetter) {
  $driveRoot = "$($partition.DriveLetter):\"
}
if (-not $driveRoot -and $partition.AccessPaths) {
  $driveRoot = $partition.AccessPaths | Where-Object { $_ -match '^[A-Z]:\\$' } | Select-Object -First 1
}
if (-not $driveRoot) {
  $volume = Get-Volume -Partition $partition -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($volume -and $volume.DriveLetter) {
    $driveRoot = "$($volume.DriveLetter):\"
  }
}
if (-not $driveRoot) { throw 'No mounted volume with drive letter was found.' }
$driveRoot
""";

        var output = await ExecutePowerShellCommandAsync(script, cancellationToken);
        return NormalizeDriveRoot(output);
    }

    internal static async Task DismountDiskImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        await ExecutePowerShellCommandAsync($"Dismount-DiskImage -ImagePath '{EscapePowerShellString(imagePath)}'", cancellationToken);
        await WaitForDiskImageDetachedAsync(imagePath, cancellationToken);
    }

    private static async Task CleanupStaleScratchAsync(string executionRoot, CancellationToken cancellationToken)
    {
        foreach (var root in new[]
                 {
                     Path.Combine(executionRoot, "_iso-tmp"),
                     Path.Combine(executionRoot, "_job-scratch")
                 })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var isoPath in Directory.EnumerateFiles(root, "*.iso", SearchOption.AllDirectories))
            {
                try
                {
                    await TryDismountDiskImageAsync(isoPath, cancellationToken);
                    await SafeDeleteDiskImageFileAsync(isoPath, cancellationToken);
                }
                catch
                {
                    // Best-effort cleanup. A stale file should not block the next unique scratch path.
                }
            }

            TryDeleteDirectoryTree(root);
        }
    }

    private static void TryDeleteDirectoryTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                }
            }
            catch
            {
            }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(root).Any())
            {
                Directory.Delete(root, recursive: false);
            }
        }
        catch
        {
        }
    }

    private static async Task TryDismountDiskImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        try
        {
            await ExecutePowerShellCommandAsync($$"""
$mount = Get-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}' -ErrorAction SilentlyContinue
if ($mount -and $mount.Attached) {
  Dismount-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}'
}
""", cancellationToken);

            await WaitForDiskImageDetachedAsync(imagePath, cancellationToken);
        }
        catch
        {
        }
    }

    private static async Task WaitForDiskImageDetachedAsync(string imagePath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await ExecutePowerShellCommandAsync($$"""
$mount = Get-DiskImage -ImagePath '{{EscapePowerShellString(imagePath)}}' -ErrorAction SilentlyContinue
if (-not $mount -or -not $mount.Attached) { 'detached' } else { 'attached' }
""", cancellationToken);

            if (string.Equals(result.Trim(), "detached", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        throw new InvalidOperationException($"Disk image '{imagePath}' is still attached after dismount retries.");
    }

    private static async Task SafeDeleteDiskImageFileAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
        {
            return;
        }

        await WaitForFileUnlockedAsync(imagePath, cancellationToken);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    File.Delete(imagePath);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            }
        }
    }

    private static async Task WaitForFileUnlockedAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
    }

    private static async Task ExtractDiskImageAsync(string imagePath, string extractRoot, CancellationToken cancellationToken)
    {
        var sevenZip = FindSevenZipExecutable();
        if (sevenZip is null)
        {
            throw new InvalidOperationException(
                $"Disk image '{imagePath}' requires extraction support. Install 7-Zip and ensure 7z.exe is available in PATH or under Program Files.");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = sevenZip,
                Arguments = $"x \"{imagePath}\" -o\"{extractRoot}\" -y",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"7-Zip extraction failed for '{imagePath}' with code {process.ExitCode}: {stderr}{stdout}");
        }
    }

    private static string ResolveExtractionSourcePath(string imagePath)
    {
        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        var directory = Path.GetDirectoryName(imagePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(imagePath);

        return extension switch
        {
            ".cue" => ResolveCompanionFile(directory, baseName, ".bin", imagePath),
            ".mds" => ResolveCompanionFile(directory, baseName, ".mdf", imagePath),
            ".ccd" => ResolveCompanionFile(directory, baseName, ".img", imagePath),
            _ => imagePath
        };
    }

    private static string ResolveStandaloneMountPath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new InvalidOperationException("No image path was provided.");
        }

        if (Directory.Exists(imagePath))
        {
            throw new InvalidOperationException("Manual mount expects a file path, not a folder.");
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image source was not found at '{imagePath}'.");
        }

        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        var directory = Path.GetDirectoryName(imagePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(imagePath);

        return extension switch
        {
            ".bin" => ResolveCompanionFile(directory, baseName, ".cue", imagePath),
            ".mdf" => ResolveCompanionFile(directory, baseName, ".mds", imagePath),
            ".img" when HasCompanionFile(directory, baseName, ".ccd") => ResolveCompanionFile(directory, baseName, ".ccd", imagePath),
            _ => imagePath
        };
    }

    private static string ResolveCompanionFile(string directory, string baseName, string expectedExtension, string fallbackPath)
    {
        var exactPath = Path.Combine(directory, $"{baseName}{expectedExtension}");
        if (File.Exists(exactPath))
        {
            return exactPath;
        }

        var sibling = Directory.EnumerateFiles(directory, $"{baseName}.*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(Path.GetExtension(path), expectedExtension, StringComparison.OrdinalIgnoreCase));

        return sibling ?? fallbackPath;
    }

    private static bool HasCompanionFile(string directory, string baseName, string expectedExtension)
    {
        var exactPath = Path.Combine(directory, $"{baseName}{expectedExtension}");
        if (File.Exists(exactPath))
        {
            return true;
        }

        return Directory.EnumerateFiles(directory, $"{baseName}.*", SearchOption.TopDirectoryOnly)
            .Any(path => string.Equals(Path.GetExtension(path), expectedExtension, StringComparison.OrdinalIgnoreCase));
    }

    internal static string? FindSevenZipExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
            "7z.exe"
        };

        return candidates.FirstOrDefault(candidate =>
            candidate.Contains(Path.DirectorySeparatorChar)
                ? File.Exists(candidate)
                : CommandExists(candidate));
    }

    private static bool CommandExists(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ExecutePowerShellCommandAsync(string command, CancellationToken cancellationToken)
    {
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"gamarr-agent-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(tempScriptPath, command, Encoding.UTF8, cancellationToken);

        try
        {
            var process = new Process
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
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim());
            }

            return stdout.Trim();
        }
        finally
        {
            if (File.Exists(tempScriptPath))
            {
                File.Delete(tempScriptPath);
            }
        }
    }

    private static string EscapePowerShellString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static async Task<(string ScriptPath, Action? Cleanup)> ResolvePowerShellScriptAsync(
        NextJobResponse job,
        string executionRoot,
        IReadOnlyCollection<PreparedMedia> media,
        CancellationToken cancellationToken)
    {
        var scriptPath = ExpandKnownPlaceholders(job.InstallScriptPath, executionRoot, media.FirstOrDefault()?.RootPath ?? executionRoot, media);
        if (!scriptPath.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"PowerShell install script was not found at '{scriptPath}'.");
            }

            return (scriptPath, null);
        }

        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"gamarr-builtin-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(tempScriptPath, BuildBuiltInPowerShellScript(scriptPath), Encoding.UTF8, cancellationToken);
        return (tempScriptPath, () =>
        {
            if (File.Exists(tempScriptPath))
            {
                File.Delete(tempScriptPath);
            }
        });
    }

    private static string BuildBuiltInPowerShellScript(string builtinScriptPath) =>
        builtinScriptPath.Equals("builtin:install-wincdemu", StringComparison.OrdinalIgnoreCase)
            ? """
$ErrorActionPreference = 'Stop'

$existingCandidates = @(
  "$env:ProgramFiles(x86)\WinCDEmu\batchmnt.exe",
  "$env:ProgramFiles\WinCDEmu\batchmnt.exe"
) | Where-Object { $_ -and (Test-Path $_) }

if ($existingCandidates.Count -gt 0) {
  Write-Output "WinCDEmu is already installed at $($existingCandidates[0])."
  return
}

$downloadUrl = 'https://github.com/sysprogs/WinCDEmu/releases/download/v4.1/WinCDEmu-4.1.exe'
$downloadRoot = Join-Path $env:TEMP 'GamarrPrereqs'
$installerPath = Join-Path $downloadRoot 'WinCDEmu-4.1.exe'

New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null

if (-not (Test-Path $installerPath)) {
  Write-Output "Downloading WinCDEmu installer from official release..."
  Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $installerPath
} else {
  Write-Output "Reusing downloaded WinCDEmu installer at $installerPath."
}

if (-not (Test-Path $installerPath)) {
  throw "WinCDEmu installer could not be downloaded to $installerPath."
}

Write-Output "Launching WinCDEmu installer with elevation. Complete the installer on this machine."

try {
  $process = Start-Process -FilePath $installerPath -Verb RunAs -Wait -PassThru
} catch {
  throw "WinCDEmu installation requires elevation. Approve the UAC prompt and retry if it was cancelled. $($_.Exception.Message)"
}

Start-Sleep -Seconds 2

$installedPath = @(
  "$env:ProgramFiles(x86)\WinCDEmu\batchmnt.exe",
  "$env:ProgramFiles\WinCDEmu\batchmnt.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $installedPath) {
  if ($process.ExitCode -ne 0) {
    throw "WinCDEmu installer exited with code $($process.ExitCode) and batchmnt.exe was not found afterwards."
  }

  throw "WinCDEmu installer finished but batchmnt.exe was not found afterwards."
}

Write-Output "WinCDEmu installed successfully at $installedPath."
"""
        : builtinScriptPath.Equals("builtin:library-import", StringComparison.OrdinalIgnoreCase) ||
        builtinScriptPath.Equals("builtin:portable-copy", StringComparison.OrdinalIgnoreCase)
            ? """
$ErrorActionPreference = 'Stop'
$installRoot = $env:GAMARR_INSTALL_ROOT
$mediaRoot = $env:GAMARR_PRIMARY_MEDIA_ROOT
$payloadRoot = Join-Path $installRoot 'payload'
$installedRoot = Join-Path $installRoot 'installed'
if (-not (Test-Path $mediaRoot)) { throw "Primary media root '$mediaRoot' was not found." }
New-Item -ItemType Directory -Path $installedRoot -Force | Out-Null
$markerPath = Join-Path $installedRoot 'gamarr-library-import.marker'
Set-Content -Path $markerPath -Value $mediaRoot -Encoding UTF8
Write-Output "Registered in-place library folder at $mediaRoot."
"""
        : builtinScriptPath.Equals("builtin:auto-install", StringComparison.OrdinalIgnoreCase) ||
          builtinScriptPath.Equals("builtin:needs-review", StringComparison.OrdinalIgnoreCase)
            ? """
$ErrorActionPreference = 'Stop'

function Resolve-AutorunCommand {
  param([string]$RootPath)

  $autorunPath = Join-Path $RootPath 'autorun.inf'
  if (-not (Test-Path $autorunPath)) { return $null }

  foreach ($line in Get-Content -Path $autorunPath) {
    if ($line -match '^\s*(open|shellexecute)\s*=\s*(.+?)\s*$') {
      return $matches[2].Trim().Trim('"')
    }
  }

  return $null
}

function Get-InstallerFamily {
  param([string]$InstallerPath)

  if ($InstallerPath.EndsWith('.msi', [System.StringComparison]::OrdinalIgnoreCase)) {
    return 'Msi'
  }

  $fileName = [System.IO.Path]::GetFileName($InstallerPath)
  try {
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($InstallerPath)
    $signature = "$($version.FileDescription) $($version.CompanyName) $($version.ProductName)"
    if ($signature -match 'Inno Setup') { return 'Inno' }
    if ($signature -match 'Nullsoft|NSIS') { return 'Nsis' }
    if ($signature -match 'InstallShield') { return 'InstallShield' }
  } catch {
  }

  try {
    $stream = [System.IO.File]::OpenRead($InstallerPath)
    try {
      $bufferSize = [Math]::Min([int]$stream.Length, 1048576)
      if ($bufferSize -gt 0) {
        $buffer = New-Object byte[] $bufferSize
        [void]$stream.Read($buffer, 0, $bufferSize)
        $ascii = [System.Text.Encoding]::Latin1.GetString($buffer)
        $unicode = [System.Text.Encoding]::Unicode.GetString($buffer)
        $probe = "$ascii $unicode"
        if ($probe -match 'Inno Setup') { return 'Inno' }
        if ($probe -match 'Nullsoft|NSIS') { return 'Nsis' }
        if ($probe -match 'InstallShield') { return 'InstallShield' }
      }
    } finally {
      $stream.Dispose()
    }
  } catch {
  }

  return 'Unknown'
}

function Test-IgnoredInstallerPath {
  param([string]$InstallerPath)

  $normalized = $InstallerPath.ToLowerInvariant()
  return $normalized -match '\\(directx|dxmedia|redist|redistributable|redistributables|support|crack|keygen)\\'
}

function Get-InstallerCandidateRank {
  param(
    [string]$RootPath,
    [string]$InstallerPath,
    [string]$Family,
    [bool]$PreferAutorun = $false
  )

  if (-not (Test-Path $InstallerPath) -or (Test-IgnoredInstallerPath -InstallerPath $InstallerPath)) {
    return -100
  }

  $fileName = [System.IO.Path]::GetFileName($InstallerPath)
  $relativePath = [System.IO.Path]::GetRelativePath($RootPath, $InstallerPath)
  $depth = ($relativePath -split '[\\/]').Length - 1
  $score = if ($PreferAutorun) { 180 } else { 100 - ([Math]::Max(0, $depth) * 10) }

  switch -Regex ($fileName.ToLowerInvariant()) {
    '^setup\.exe$|^install\.exe$|^installer\.exe$|^autorun\.exe$' { $score += 80; break }
    'setup|install|autorun' { $score += 40; break }
  }

  switch ($Family) {
    'Msi' { $score += 50; break }
    'Inno' { $score += 40; break }
    'Nsis' { $score += 30; break }
    'InstallShield' { $score += 20; break }
  }

  return $score
}

function Resolve-InstallerCandidate {
  param([string]$RootPath)

  $msi = Get-ChildItem -Path $RootPath -Recurse -Filter *.msi -File -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($msi) { return $msi.FullName }

  $autorunCommand = Resolve-AutorunCommand -RootPath $RootPath
  if ($autorunCommand) {
    $autorunCandidate = ($autorunCommand -split '\s+')[0].Trim('"')
    $autorunPath = Join-Path $RootPath $autorunCandidate
    if (Test-Path $autorunPath) {
      if (-not (Test-IgnoredInstallerPath -InstallerPath $autorunPath)) {
        return $autorunPath
      }
    }
  }

  $exe = Get-ChildItem -Path $RootPath -Recurse -Include setup.exe,install.exe,installer.exe,autorun.exe -File -ErrorAction SilentlyContinue |
    Sort-Object FullName |
    ForEach-Object {
      $family = Get-InstallerFamily -InstallerPath $_.FullName
      [PSCustomObject]@{
        Path = $_.FullName
        Family = $family
        Rank = Get-InstallerCandidateRank -RootPath $RootPath -InstallerPath $_.FullName -Family $family
      }
    } |
    Sort-Object -Property @{ Expression = 'Rank'; Descending = $true }, @{ Expression = 'Path'; Descending = $false } |
    Select-Object -First 1

  if ($exe -and $exe.Rank -gt 0) { return $exe.Path }

  # Fallback: scan all .exe files in the root directory and accept obvious bootstrap installers
  # even when the family is unknown.
  $rootFamilyExe = Get-ChildItem -Path $RootPath -Filter *.exe -File -ErrorAction SilentlyContinue |
    ForEach-Object {
      $f = Get-InstallerFamily -InstallerPath $_.FullName
      [PSCustomObject]@{
        Path = $_.FullName
        Family = $f
        Rank = Get-InstallerCandidateRank -RootPath $RootPath -InstallerPath $_.FullName -Family $f
      }
    } |
    Where-Object { $_.Rank -gt 0 } |
    Sort-Object -Property @{ Expression = 'Rank'; Descending = $true }, @{ Expression = 'Path'; Descending = $false } |
    Select-Object -First 1
  if ($rootFamilyExe) { return $rootFamilyExe.Path }

  return $null
}

function Resolve-LaunchCandidate {
  param(
    [string]$RootPath,
    [string]$PackageName
  )

  $titleTokens = @()
  if ($PackageName) {
    $titleTokens = ($PackageName -replace '[^A-Za-z0-9 ]', ' ').Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries) |
      Where-Object { $_.Length -ge 3 } |
      ForEach-Object { $_.ToLowerInvariant() }
  }

  $winner = Get-ChildItem -Path $RootPath -Recurse -Filter *.exe -File -ErrorAction SilentlyContinue |
    Where-Object {
      $path = $_.FullName.ToLowerInvariant()
      $name = $_.Name.ToLowerInvariant()
      $path -notmatch '\\(directx|redist|redistributable|gamespy|support|docs?|manual|patch|update|crack|keygen|starforce)\\' -and
      $name -notmatch '^(setup|install|autorun|unins|uninstall|dxsetup|test|protect|sfclean)' -and
      $name -notmatch '(setup|config|protect|test|launcherconfig)'
    } |
    ForEach-Object {
      $score = 0
      $name = $_.BaseName.ToLowerInvariant()
      $path = $_.FullName.ToLowerInvariant()
      $signature = ''

      try {
        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName)
        $signature = "$($version.FileDescription) $($version.ProductName)".ToLowerInvariant()
      } catch {
      }

      foreach ($token in $titleTokens) {
        if ($name.Contains($token) -or $path.Contains($token) -or $signature.Contains($token)) {
          $score += 20
        }
      }

      if ($path.Contains('\installdata\')) { $score += 15 }
      if ($signature) { $score += 10 }
      if ($name -match 'game|play|launcher') { $score += 10 }
      if ($_.Length -ge 50MB) { $score += 30 }
      elseif ($_.Length -ge 10MB) { $score += 20 }
      elseif ($_.Length -ge 1MB) { $score += 10 }
      if ($name -match 'setup|config|protect|test|oggdec|hardware|clean|dx') { $score -= 50 }
      if ($signature -match 'setup|configuration|config|protection|test|directx') { $score -= 30 }
      if ($_.DirectoryName -and $_.DirectoryName.Length -lt ($RootPath.Length + 32)) { $score += 5 }

      [PSCustomObject]@{
        Path = $_.FullName
        Score = $score
      }
    } |
    Sort-Object -Property @{ Expression = 'Score'; Descending = $true }, @{ Expression = 'Path'; Descending = $false } |
    Select-Object -First 1

  if ($winner -and $winner.Score -gt 0) { return $winner.Path }

  return $null
}

$installRoot = $env:GAMARR_INSTALL_ROOT
$mediaRoot = $env:GAMARR_PRIMARY_MEDIA_ROOT
$installedRoot = $env:GAMARR_INSTALLED_ROOT
$payloadRoot = $env:GAMARR_PAYLOAD_ROOT
$packageName = $env:GAMARR_PACKAGE_NAME
$persistedInstallerPath = $env:GAMARR_INSTALLER_PATH
$persistedFamily = $env:GAMARR_INSTALLER_FAMILY
$persistedArgs = $env:GAMARR_INSTALLER_ARGS
$markerPath = Join-Path $installedRoot 'gamarr-library-import.marker'
$launchPathMarker = Join-Path $installedRoot 'gamarr-launch-path.txt'

if (-not (Test-Path $mediaRoot)) { throw "Primary media root '$mediaRoot' was not found." }
New-Item -ItemType Directory -Path $installedRoot -Force | Out-Null
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

$installerPath = $null
if ($persistedInstallerPath) {
  $candidatePath = Join-Path $mediaRoot $persistedInstallerPath
  if (Test-Path $candidatePath) {
    $installerPath = $candidatePath
  }
}

if (-not $installerPath) {
  $installerPath = Resolve-InstallerCandidate -RootPath $mediaRoot
}
if (-not $installerPath) {
  throw "No installable entrypoint was found in '$mediaRoot'. Review is required."
}

$family = if ($persistedFamily -and $persistedFamily -ne 'Unknown') { $persistedFamily } else { Get-InstallerFamily -InstallerPath $installerPath }
Write-Output "Resolved installer: $installerPath ($family)"

switch ($family) {
  'Msi' {
    $arguments = if ($persistedArgs) {
      @('/i', "`"$installerPath`"") + ($persistedArgs -split '\s+')
    } else {
      @('/i', "`"$installerPath`"", '/qn', '/norestart')
    }
    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -WorkingDirectory ([System.IO.Path]::GetDirectoryName($installerPath)) -Wait -PassThru
  }
  'Inno' {
    $arguments = if ($persistedArgs) { $persistedArgs -split '\s+' } else { @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-') }
    $process = Start-Process -FilePath $installerPath -ArgumentList $arguments -WorkingDirectory ([System.IO.Path]::GetDirectoryName($installerPath)) -Wait -PassThru
  }
  'Nsis' {
    $arguments = if ($persistedArgs) { $persistedArgs -split '\s+' } else { @('/S') }
    $process = Start-Process -FilePath $installerPath -ArgumentList $arguments -WorkingDirectory ([System.IO.Path]::GetDirectoryName($installerPath)) -Wait -PassThru
  }
  'InstallShield' {
    if ($persistedArgs) {
      $arguments = $persistedArgs -split '\s+'
      $process = Start-Process -FilePath $installerPath -ArgumentList $arguments -WorkingDirectory ([System.IO.Path]::GetDirectoryName($installerPath)) -Wait -PassThru
    } else {
      Write-Output "InstallShield installer '$installerPath' — launching interactively. Complete the installation on this machine."
      $process = Start-Process -FilePath $installerPath -WorkingDirectory ([System.IO.Path]::GetDirectoryName($installerPath)) -Wait -PassThru
    }
  }
  default {
    if ($persistedArgs) {
      $arguments = $persistedArgs -split '\s+'
      Write-Output "Installer family '$family' — launching '$installerPath' with persisted args: $($arguments -join ' ')"
      $process = Start-Process -FilePath $installerPath -ArgumentList $arguments -WorkingDirectory ([System.IO.Path]::GetDirectoryName($installerPath)) -Wait -PassThru
    } else {
      Write-Output "Installer family '$family' unrecognised for '$installerPath' — launching installer interactively. Complete the installation on this machine."
      $process = Start-Process -FilePath $installerPath -WorkingDirectory ([System.IO.Path]::GetDirectoryName($installerPath)) -Wait -PassThru
    }
  }
}

if ($process.ExitCode -ne 0 -and $family -ne 'Unknown' -and $persistedArgs) {
  throw "Installer exited with code $($process.ExitCode)."
} elseif ($process.ExitCode -ne 0) {
  Write-Warning "Installer exited with code $($process.ExitCode) — treating as complete (interactive or unknown family)."
}

Set-Content -Path $markerPath -Value $installerPath -Encoding UTF8
$launchCandidate = Resolve-LaunchCandidate -RootPath $mediaRoot -PackageName $packageName
if ($launchCandidate) {
  Set-Content -Path $launchPathMarker -Value $launchCandidate -Encoding UTF8
  Write-Output "Discovered launch candidate: $launchCandidate"
}
Write-Output "Installer completed successfully."
"""
        : throw new InvalidOperationException($"Unsupported built-in PowerShell script '{builtinScriptPath}'.");

    private static string NormalizeScratchPolicy(string scratchPolicy) =>
        scratchPolicy switch
        {
            "Persistent" => "Persistent",
            "Prompt" => "Prompt",
            _ => "Temporary"
        };

    private static bool ShouldMountSource(string sourceKind, string extension) =>
        sourceKind switch
        {
            "MountedVolume" => true,
            "ExtractedWorkspace" => false,
            "DirectFolder" => false,
            _ => IsNativeMountableImage(extension)
        };

    private static bool ShouldExtractSource(string sourceKind, string extension) =>
        sourceKind switch
        {
            "ExtractedWorkspace" => true,
            "MountedVolume" => false,
            "DirectFolder" => false,
            _ => IsExtractableDiskImage(extension)
        };

    private static string ComputeStablePathToken(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes[..8]);
    }

    private static string ToDriveRoot(string imagePath, string output)
    {
        var driveLetters = JsonSerializer.Deserialize<string[]>(NormalizeJsonArray(output)) ?? [];
        var driveLetter = driveLetters.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            throw new InvalidOperationException($"Disk image '{imagePath}' was mounted but no drive letter was detected.");
        }

        return NormalizeDriveRoot(driveLetter);
    }

    private static string NormalizeDriveRoot(string value)
    {
        var trimmed = value.Trim().Trim('"');
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            trimmed = $"{trimmed}:";
        }

        return trimmed.EndsWith(@"\", StringComparison.Ordinal) ? trimmed : $"{trimmed}\\";
    }

    private static string NormalizeJsonArray(string value) =>
        value.StartsWith("[", StringComparison.Ordinal) ? value : $"[{JsonSerializer.Serialize(value)}]";

    private static RegistryKey? OpenRegistryKey(string keyPath)
    {
        var normalized = keyPath.Replace('/', '\\');
        var separator = normalized.IndexOf('\\');
        var hiveName = separator >= 0 ? normalized[..separator] : normalized;
        var subKey = separator >= 0 ? normalized[(separator + 1)..] : string.Empty;

        RegistryKey? root = hiveName.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => null
        };

        return root?.OpenSubKey(subKey);
    }

    private static bool FindUninstallEntry(string displayName) =>
        FindUninstallEntryDetails(displayName) is not null;

    private static UninstallEntryDetails? FindUninstallEntryDetails(string displayName)
    {
        return FindUninstallEntryInHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", displayName) ??
               FindUninstallEntryInHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", displayName) ??
               FindUninstallEntryInHive(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", displayName);
    }

    private static UninstallEntryDetails? FindUninstallEntryInHive(RegistryKey hive, string subKeyPath, string displayName)
    {
        using var uninstallRoot = hive.OpenSubKey(subKeyPath);
        if (uninstallRoot is null)
        {
            return null;
        }

        foreach (var childName in uninstallRoot.GetSubKeyNames())
        {
            using var child = uninstallRoot.OpenSubKey(childName);
            var currentDisplayName = child?.GetValue("DisplayName") as string;
            if (!string.IsNullOrWhiteSpace(currentDisplayName) &&
                currentDisplayName.Contains(displayName, StringComparison.OrdinalIgnoreCase))
            {
                return new UninstallEntryDetails(
                    currentDisplayName,
                    child?.GetValue("InstallLocation") as string,
                    child?.GetValue("DisplayIcon") as string,
                    child?.GetValue("UninstallString") as string,
                    child?.GetValue("QuietUninstallString") as string);
            }
        }

        return null;
    }

    private sealed record UninstallEntryDetails(
        string DisplayName,
        string? InstallLocation,
        string? DisplayIcon,
        string? UninstallString,
        string? QuietUninstallString);

    private sealed record ValidationOutcome(
        bool Installed,
        string Summary,
        IReadOnlyCollection<JobLogRequest> Logs,
        string? LaunchPath,
        string? InstallLocation,
        string? UninstallCommand);

    private sealed record PreparedMedia(
        string Label,
        string MediaType,
        string SourcePath,
        string RootPath,
        int? DiscNumber,
        string? EntrypointHint,
        string SourceKind,
        string ScratchPolicy,
        string PrepareMethod);

    private sealed class PreparedMediaSession : IAsyncDisposable
    {
        public PreparedMediaSession(IReadOnlyCollection<PreparedMedia> media, IReadOnlyCollection<Func<CancellationToken, Task>> cleanup)
        {
            Media = media;
            _cleanup = cleanup;
        }

        private readonly IReadOnlyCollection<Func<CancellationToken, Task>> _cleanup;

        public IReadOnlyCollection<PreparedMedia> Media { get; }

        public async ValueTask DisposeAsync()
        {
            foreach (var action in _cleanup.Reverse())
            {
                try
                {
                    await action(CancellationToken.None);
                }
                catch
                {
                    // Cleanup failures should not hide install results.
                }
            }
        }
    }

    private static bool IsExecutableLaunchPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ReportAsync(
        Func<JobEventRequest, Task> reportEvent,
        int sequence,
        JobState state,
        string message,
        IReadOnlyCollection<JobLogRequest> logs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await reportEvent(new JobEventRequest(sequence, state, message, logs));
    }

    private static Task ReportAsync(
        Func<JobEventRequest, Task> reportEvent,
        int sequence,
        JobState state,
        string message,
        JobLogRequest log,
        CancellationToken cancellationToken) =>
        ReportAsync(reportEvent, sequence, state, message, [log], cancellationToken);
}
