using System.Text.Json;
using Gamarr.Application.Contracts;

namespace Gamarr.Infrastructure.Services;

internal static class PackageManifestBuilder
{
    private const string CurrentManifestVersion = "gamarr.package/v1";

    public static (string FormatVersion, string ManifestJson) Build(CreatePackageRequest request)
    {
        var manifest = new
        {
            manifestVersion = CurrentManifestVersion,
            package = new
            {
                slug = request.Slug.Trim(),
                name = request.Name.Trim(),
                description = request.Description.Trim(),
                notes = request.Notes.Trim(),
                tags = request.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray(),
                genres = request.Genres.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray(),
                studio = request.Studio.Trim(),
                releaseYear = request.ReleaseYear,
                coverImagePath = request.CoverImagePath
            },
            version = new
            {
                versionLabel = request.Version.VersionLabel.Trim(),
                supportedOs = request.Version.SupportedOs.Trim(),
                architecture = request.Version.Architecture.ToString(),
                timeoutSeconds = request.Version.TimeoutSeconds,
                notes = request.Version.Notes.Trim(),
                installPlan = new
                {
                    strategy = request.Version.InstallStrategy.Trim(),
                    installerFamily = request.Version.InstallerFamily.Trim(),
                    installerPath = request.Version.InstallerPath,
                    silentArguments = request.Version.SilentArguments,
                    diagnostics = request.Version.InstallDiagnostics.Trim()
                },
                launchExecutablePath = request.Version.LaunchExecutablePath,
                scripts = new
                {
                    install = new
                    {
                        kind = request.Version.InstallScriptKind.ToString(),
                        path = request.Version.InstallScriptPath.Trim()
                    },
                    uninstall = string.IsNullOrWhiteSpace(request.Version.UninstallScriptPath)
                        ? null
                        : new
                        {
                            kind = request.Version.InstallScriptKind.ToString(),
                            path = request.Version.UninstallScriptPath!.Trim(),
                            arguments = request.Version.UninstallArguments
                        }
                },
                media = request.Version.Media.Select(x => new
                {
                    type = x.MediaType.ToString(),
                    label = x.Label.Trim(),
                    path = x.Path.Trim(),
                    discNumber = x.DiscNumber,
                    entrypointHint = string.IsNullOrWhiteSpace(x.EntrypointHint) ? null : x.EntrypointHint.Trim(),
                    sourceKind = x.SourceKind.ToString(),
                    scratchPolicy = x.ScratchPolicy.ToString()
                }).ToArray(),
                detectionRules = request.Version.DetectionRules.Select(x => new
                {
                    ruleType = x.RuleType.Trim(),
                    value = x.Value.Trim()
                }).ToArray(),
                prerequisites = request.Version.Prerequisites.Select(x => new
                {
                    name = x.Name.Trim(),
                    notes = x.Notes.Trim()
                }).ToArray()
            }
        };

        return (CurrentManifestVersion, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
