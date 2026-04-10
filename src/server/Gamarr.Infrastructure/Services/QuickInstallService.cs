using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;
using Gamarr.Domain.Enums;

namespace Gamarr.Infrastructure.Services;

public sealed class QuickInstallService(IPackageService packageService, IJobService jobService)
{
    public async Task<QuickInstallResponse> QuickInstallAsync(QuickInstallRequest request, CancellationToken cancellationToken)
    {
        var isoPath = request.IsoPath.Trim();
        var filename = Path.GetFileNameWithoutExtension(isoPath);
        var label = string.IsNullOrWhiteSpace(request.Label) ? filename : request.Label.Trim();
        var slug = GenerateSlug(label);

        var packageRequest = new CreatePackageRequest(
            Slug: slug,
            Name: label,
            Description: string.Empty,
            Notes: $"Quick install from ISO: {isoPath}",
            Tags: [],
            Genres: [],
            Studio: string.Empty,
            ReleaseYear: null,
            CoverImagePath: null,
            Version: new CreatePackageVersionRequest(
                VersionLabel: "1.0",
                SupportedOs: "Windows 10, Windows 11",
                Architecture: ArchitectureKind.X64,
                InstallScriptKind: InstallScriptKind.PowerShell,
                InstallScriptPath: "builtin:auto-install",
                UninstallScriptPath: null,
                UninstallArguments: null,
                TimeoutSeconds: 3600,
                Notes: string.Empty,
                InstallStrategy: "AutoInstall",
                InstallerFamily: "Unknown",
                InstallerPath: null,
                SilentArguments: null,
                InstallDiagnostics: string.Empty,
                LaunchExecutablePath: null,
                Media:
                [
                    new CreatePackageMediaRequest(
                        MediaType: MediaType.Iso,
                        Label: "Disc 1",
                        Path: isoPath,
                        DiscNumber: 1,
                        EntrypointHint: null,
                        SourceKind: PackageSourceKind.Auto,
                        ScratchPolicy: ScratchPolicy.Temporary)
                ],
                DetectionRules: [],
                Prerequisites: []));

        var package = await packageService.CreateAsync(packageRequest, cancellationToken);
        var version = package.Versions.First();

        var job = await jobService.CreateAsync(
            new CreateJobRequest(package.Id, request.MachineId, JobActionType.Install, "web-ui"),
            cancellationToken);

        return new QuickInstallResponse(job.Id, package.Id);
    }

    private static string GenerateSlug(string label)
    {
        var slug = label.ToLowerInvariant();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = slug.Trim('-');
        if (slug.Length > 88) slug = slug[..88].TrimEnd('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "quick-install";
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{slug}-{suffix}";
    }
}
