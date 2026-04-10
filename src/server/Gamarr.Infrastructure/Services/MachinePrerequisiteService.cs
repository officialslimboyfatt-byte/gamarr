using Gamarr.Application.Contracts;
using Gamarr.Application.Exceptions;
using Gamarr.Application.Interfaces;
using Gamarr.Domain.Entities;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamarr.Infrastructure.Services;

public sealed class MachinePrerequisiteService(
    GamarrDbContext dbContext,
    IJobService jobService)
{
    private const string WinCDEmuPackageSlug = "system-wincdemu-prerequisite";

    public async Task<JobResponse> QueueWinCDEmuInstallAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machine = await dbContext.Machines
            .Include(x => x.Capabilities)
            .FirstOrDefaultAsync(x => x.Id == machineId, cancellationToken)
            ?? throw new AppNotFoundException("Machine not found.");

        if (machine.Capabilities.Any(x => string.Equals(x.Capability, "wincdemu", StringComparison.OrdinalIgnoreCase)))
        {
            throw new AppConflictException("WinCDEmu is already installed on this machine.");
        }

        var package = await EnsureWinCDEmuPackageAsync(cancellationToken);
        return await jobService.CreateAsync(
            new CreateJobRequest(package.Id, machineId, JobActionType.Install, "machine-prerequisite"),
            cancellationToken);
    }

    private async Task<Package> EnsureWinCDEmuPackageAsync(CancellationToken cancellationToken)
    {
        var existing = await dbContext.Packages
            .Include(p => p.Versions)
            .FirstOrDefaultAsync(p => p.Slug == WinCDEmuPackageSlug, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var request = new CreatePackageRequest(
            Slug: WinCDEmuPackageSlug,
            Name: "WinCDEmu (System Prerequisite)",
            Description: "Managed system prerequisite used to install WinCDEmu on target machines.",
            Notes: "System package for one-click WinCDEmu installation.",
            Tags: ["system", "prerequisite", "wincdemu"],
            Genres: [],
            Studio: "Sysprogs",
            ReleaseYear: null,
            CoverImagePath: null,
            Version: new CreatePackageVersionRequest(
                VersionLabel: "managed",
                SupportedOs: "Windows 10, Windows 11",
                Architecture: ArchitectureKind.X64,
                InstallScriptKind: InstallScriptKind.PowerShell,
                InstallScriptPath: "builtin:install-wincdemu",
                UninstallScriptPath: null,
                UninstallArguments: null,
                TimeoutSeconds: 1800,
                Notes: "Runs a managed WinCDEmu installation on the selected machine.",
                InstallStrategy: "MachinePrerequisite",
                InstallerFamily: "Winget",
                InstallerPath: null,
                SilentArguments: null,
                InstallDiagnostics: "Managed prerequisite install via WinGet.",
                LaunchExecutablePath: null,
                Media: [],
                DetectionRules:
                [
                    new CreateDetectionRuleRequest("FileExists", @"C:\Program Files (x86)\WinCDEmu\batchmnt.exe"),
                    new CreateDetectionRuleRequest("FileExists", @"C:\Program Files\WinCDEmu\batchmnt.exe")
                ],
                Prerequisites: []),
            MetadataProvider: null,
            MetadataSourceUrl: null,
            MetadataSelectionKind: "System");

        var manifest = PackageManifestBuilder.Build(request);

        var package = new Package
        {
            Slug = request.Slug,
            Name = request.Name,
            Description = request.Description,
            Notes = request.Notes,
            TagsSerialized = string.Join(';', request.Tags),
            GenresSerialized = string.Empty,
            Studio = request.Studio,
            MetadataSelectionKind = request.MetadataSelectionKind,
            IsArchived = true,
            ArchivedReason = "System prerequisite package.",
            ArchivedAtUtc = DateTimeOffset.UtcNow,
            Versions =
            {
                new PackageVersion
                {
                    VersionLabel = request.Version.VersionLabel,
                    SupportedOs = request.Version.SupportedOs,
                    Architecture = request.Version.Architecture,
                    InstallScriptKind = request.Version.InstallScriptKind,
                    InstallScriptPath = request.Version.InstallScriptPath,
                    UninstallScriptPath = request.Version.UninstallScriptPath,
                    UninstallArguments = request.Version.UninstallArguments,
                    ManifestFormatVersion = manifest.FormatVersion,
                    ManifestJson = manifest.ManifestJson,
                    TimeoutSeconds = request.Version.TimeoutSeconds,
                    Notes = request.Version.Notes,
                    InstallStrategy = request.Version.InstallStrategy,
                    InstallerFamily = request.Version.InstallerFamily,
                    InstallerPath = request.Version.InstallerPath,
                    SilentArguments = request.Version.SilentArguments,
                    InstallDiagnostics = request.Version.InstallDiagnostics,
                    LaunchExecutablePath = null,
                    ProcessingState = "Ready",
                    NormalizedAssetRootPath = null,
                    NormalizedAtUtc = DateTimeOffset.UtcNow,
                    NormalizationDiagnostics = "System prerequisite package. No normalization required.",
                    DetectionRules = request.Version.DetectionRules.Select(rule => new InstallDetectionRule
                    {
                        RuleType = rule.RuleType,
                        Value = rule.Value
                    }).ToList()
                }
            }
        };

        dbContext.Packages.Add(package);
        await dbContext.SaveChangesAsync(cancellationToken);
        return package;
    }
}
