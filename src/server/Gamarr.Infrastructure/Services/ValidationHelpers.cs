using Gamarr.Application.Contracts;
using Gamarr.Application.Exceptions;
using Gamarr.Domain.Enums;

namespace Gamarr.Infrastructure.Services;

internal static class ValidationHelpers
{
    public static void ValidatePackageRequest(CreatePackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            throw new AppValidationException("Package slug is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AppValidationException("Package name is required.");
        }

        if (request.ReleaseYear is < 1970 or > 2100)
        {
            throw new AppValidationException("ReleaseYear must be between 1970 and 2100 when provided.");
        }

        ValidateVersionRequest(request.Version);
    }

    public static void ValidateVersionRequest(CreatePackageVersionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VersionLabel))
        {
            throw new AppValidationException("Package version label is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SupportedOs))
        {
            throw new AppValidationException("Supported operating systems are required.");
        }

        if (string.IsNullOrWhiteSpace(request.InstallScriptPath))
        {
            throw new AppValidationException("Install script path is required.");
        }

        if (!Enum.IsDefined(typeof(InstallScriptKind), request.InstallScriptKind))
        {
            throw new AppValidationException("Install script kind is required.");
        }

        if (request.TimeoutSeconds <= 0)
        {
            throw new AppValidationException("TimeoutSeconds must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.InstallStrategy))
        {
            throw new AppValidationException("Install strategy is required.");
        }

        if (string.IsNullOrWhiteSpace(request.InstallerFamily))
        {
            throw new AppValidationException("Installer family is required.");
        }

        if (request.Media.Count == 0)
        {
            throw new AppValidationException("At least one local media reference is required.");
        }

        foreach (var media in request.Media)
        {
            if (string.IsNullOrWhiteSpace(media.Label) || string.IsNullOrWhiteSpace(media.Path))
            {
                throw new AppValidationException("Package media entries must include a label and path.");
            }

            if (!Enum.IsDefined(typeof(MediaType), media.MediaType))
            {
                throw new AppValidationException("Package media type is required.");
            }

            if (!Enum.IsDefined(typeof(PackageSourceKind), media.SourceKind))
            {
                throw new AppValidationException("Package media source kind is required.");
            }

            if (!Enum.IsDefined(typeof(ScratchPolicy), media.ScratchPolicy))
            {
                throw new AppValidationException("Package media scratch policy is required.");
            }
        }
    }

    public static void ValidateInstallPlanRequest(UpdatePackageInstallPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InstallStrategy))
        {
            throw new AppValidationException("Install strategy is required.");
        }

        if (string.IsNullOrWhiteSpace(request.InstallerFamily))
        {
            throw new AppValidationException("Installer family is required.");
        }

        foreach (var rule in request.DetectionRules)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleType) || string.IsNullOrWhiteSpace(rule.Value))
            {
                throw new AppValidationException("Detection rules must include a rule type and value.");
            }
        }
    }

    public static void ValidateMachineRegistration(RegisterMachineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StableKey))
        {
            throw new AppValidationException("Machine stable key is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AppValidationException("Machine name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            throw new AppValidationException("Machine hostname is required.");
        }
    }

    public static void ValidateHeartbeat(HeartbeatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AgentVersion))
        {
            throw new AppValidationException("Agent version is required.");
        }
    }

    public static void ValidateJobRequest(CreateJobRequest request)
    {
        if (request.PackageId == Guid.Empty || request.MachineId == Guid.Empty)
        {
            throw new AppValidationException("PackageId and MachineId are required.");
        }

        if (!Enum.IsDefined(typeof(JobActionType), request.ActionType))
        {
            throw new AppValidationException("Unsupported job action type.");
        }
    }

    public static void ValidateJobEvent(JobEventRequest request)
    {
        if (request.SequenceNumber <= 0)
        {
            throw new AppValidationException("Job event sequence number must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new AppValidationException("Job event message is required.");
        }

        foreach (var log in request.Logs)
        {
            if (string.IsNullOrWhiteSpace(log.Source) || string.IsNullOrWhiteSpace(log.Message))
            {
                throw new AppValidationException("Job logs must include source and message.");
            }
        }
    }

    public static void ValidateLibraryRootRequest(CreateLibraryRootRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new AppValidationException("Library root display name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            throw new AppValidationException("Library root path is required.");
        }
    }

    public static void ValidateMetadataSettings(UpdateMetadataSettingsRequest request)
    {
        if (request.AutoImportThreshold <= 0d || request.AutoImportThreshold >= 1d)
        {
            throw new AppValidationException("Auto-import threshold must be between 0 and 1.");
        }

        if (request.ReviewThreshold <= 0d || request.ReviewThreshold >= 1d)
        {
            throw new AppValidationException("Review threshold must be between 0 and 1.");
        }

        if (request.ReviewThreshold > request.AutoImportThreshold)
        {
            throw new AppValidationException("Review threshold must be less than or equal to the auto-import threshold.");
        }
    }

    public static void ValidateMediaManagementSettings(UpdateMediaManagementSettingsRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DefaultLibraryRootPath) && request.DefaultLibraryRootPath.Length > 1024)
        {
            throw new AppValidationException("Default library root path is too long.");
        }

        if (!string.IsNullOrWhiteSpace(request.NormalizedAssetRootPath) && request.NormalizedAssetRootPath.Length > 1024)
        {
            throw new AppValidationException("Normalized asset root path is too long.");
        }

        if (request.IncludePatterns.Any(x => x.Length > 256) || request.ExcludePatterns.Any(x => x.Length > 256))
        {
            throw new AppValidationException("Include and exclude patterns must be 256 characters or fewer.");
        }
    }
}
