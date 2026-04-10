using Gamarr.Domain.Entities;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gamarr.Infrastructure.Services;

public sealed class DemoDataSeeder(
    GamarrDbContext dbContext,
    IConfiguration configuration,
    ILogger<DemoDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var enabled = bool.TryParse(configuration["Seed:DemoDataEnabled"] ?? configuration["GAMARR_SEED_DEMO_DATA"], out var parsed) && parsed;
        if (!enabled)
        {
            return;
        }

        if (!await dbContext.Packages.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Seeding demo package.");
            var manifestJson = """
{
  "manifestVersion": "gamarr.package/v1",
  "package": {
    "slug": "half-life-demo",
    "name": "Half-Life Demo",
    "description": "Sample package using user-supplied local installer media.",
    "notes": "Replace media paths with your own local content before production use.",
    "tags": ["fps", "classic", "demo"]
  },
  "version": {
    "versionLabel": "1.0",
    "supportedOs": "Windows 10, Windows 11",
    "architecture": "X64",
    "timeoutSeconds": 1800,
    "notes": "Sample recipe for local testing.",
    "scripts": {
      "install": {
        "kind": "MockRecipe",
        "path": "C:\\GamarrMedia\\HalfLifeDemo\\install.mock"
      }
    },
    "media": [
      {
        "type": "Iso",
        "label": "Game ISO",
        "path": "C:\\GamarrMedia\\HalfLifeDemo\\half-life-demo.iso"
      }
    ],
    "detectionRules": [
      {
        "ruleType": "FileExists",
        "value": "%MOCK_INSTALL_ROOT%\\installed\\hl.exe"
      }
    ],
    "prerequisites": [
      {
        "name": ".NET Framework 4.8",
        "notes": "Only needed for legacy tooling in this sample."
      }
    ]
  }
}
""";

            dbContext.Packages.Add(new Package
            {
                Slug = "half-life-demo",
                Name = "Half-Life Demo",
                Description = "Sample package using user-supplied local installer media.",
                Notes = "Replace media paths with your own local content before production use.",
                TagsSerialized = "fps;classic;demo",
                GenresSerialized = "FPS;Sci-Fi",
                Studio = "Valve",
                ReleaseYear = 1998,
                CoverImagePath = "/covers/half-life-demo.png",
                Versions =
                {
                    new PackageVersion
                    {
                        VersionLabel = "1.0",
                        SupportedOs = "Windows 10, Windows 11",
                        Architecture = ArchitectureKind.X64,
                        InstallScriptKind = InstallScriptKind.MockRecipe,
                        InstallScriptPath = @"C:\GamarrMedia\HalfLifeDemo\install.mock",
                        ManifestFormatVersion = "gamarr.package/v1",
                        ManifestJson = manifestJson,
                        TimeoutSeconds = 1800,
                        Notes = "Sample recipe for local testing.",
                        LaunchExecutablePath = null,
                        Media =
                        {
                            new PackageMedia
                            {
                                MediaType = MediaType.Iso,
                                Label = "Game ISO",
                                Path = @"C:\GamarrMedia\HalfLifeDemo\half-life-demo.iso"
                            }
                        },
                        DetectionRules =
                        {
                            new InstallDetectionRule
                            {
                                RuleType = "FileExists",
                                Value = @"%MOCK_INSTALL_ROOT%\installed\hl.exe"
                            }
                        },
                        Prerequisites =
                        {
                            new PackagePrerequisite
                            {
                                Name = ".NET Framework 4.8",
                                Notes = "Only needed for legacy tooling in this sample."
                            }
                        }
                    }
                }
            });
        }

        if (!await dbContext.Machines.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Seeding demo machine.");
            dbContext.Machines.Add(new Machine
            {
                StableKey = "seed-machine-local",
                Name = "Local Test Rig",
                Hostname = "GAMARR-LOCAL",
                OperatingSystem = "Windows 11 Pro",
                Architecture = ArchitectureKind.X64,
                AgentVersion = "0.1.0-seed",
                Status = MachineStatus.Online,
                Capabilities =
                {
                    new MachineCapability { Capability = "iso-mount" },
                    new MachineCapability { Capability = "powershell-execution" }
                }
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
