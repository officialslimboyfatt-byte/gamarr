using Gamarr.Application.Contracts;
using Gamarr.Domain.Entities;

namespace Gamarr.Application.Services;

public static class MappingExtensions
{
    public static PackageResponse ToResponse(this Package package) =>
        new(
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
            package.MetadataProvider,
            package.MetadataSourceUrl,
            package.MetadataSelectionKind,
            package.IsArchived,
            package.ArchivedReason,
            package.ArchivedAtUtc,
            package.CreatedAtUtc,
            package.UpdatedAtUtc,
            package.Versions.OrderByDescending(v => v.IsActive).Select(ToResponse).ToArray());

    public static PackageVersionResponse ToResponse(this PackageVersion version) =>
        new(
            version.Id,
            version.VersionLabel,
            version.SupportedOs,
            version.Architecture,
            version.InstallScriptKind,
            version.InstallScriptPath,
            version.UninstallScriptPath,
            version.UninstallArguments,
            version.ManifestFormatVersion,
            version.ManifestJson,
            version.TimeoutSeconds,
            version.Notes,
            version.InstallStrategy,
            version.InstallerFamily,
            version.InstallerPath,
            version.SilentArguments,
            version.InstallDiagnostics,
            version.LaunchExecutablePath,
            version.ProcessingState,
            version.NormalizedAssetRootPath,
            version.NormalizedAtUtc,
            version.NormalizationDiagnostics,
            version.IsActive,
            version.Media.Select(m => new PackageMediaResponse(m.Id, m.MediaType, m.Label, m.Path, m.DiscNumber, m.EntrypointHint, m.SourceKind, m.ScratchPolicy)).ToArray(),
            version.DetectionRules.Select(d => new DetectionRuleResponse(d.Id, d.RuleType, d.Value)).ToArray(),
            version.Prerequisites.Select(p => new PrerequisiteResponse(p.Id, p.Name, p.Notes)).ToArray());

    public static MachineResponse ToResponse(this Machine machine) =>
        new(
            machine.Id,
            machine.StableKey,
            machine.Name,
            machine.Hostname,
            machine.OperatingSystem,
            machine.Architecture,
            machine.AgentVersion,
            machine.Status,
            machine.RegisteredAtUtc,
            machine.LastHeartbeatUtc,
            machine.Capabilities.Select(c => c.Capability).OrderBy(c => c).ToArray(),
            false,
            false,
            "Machine lifecycle metadata not loaded.",
            false);

    public static JobResponse ToResponse(this Job job) =>
        // Prefer persisted event/log timestamps to approximate last update without schema changes.
        new(
            job.Id,
            job.PackageId,
            job.PackageVersionId,
            job.MachineId,
            job.Package?.Name ?? string.Empty,
            job.PackageVersion?.VersionLabel ?? string.Empty,
            job.Machine?.Name ?? string.Empty,
            job.ActionType,
            job.State,
            job.RequestedBy,
            job.CreatedAtUtc,
            job.ClaimedAtUtc,
            job.CompletedAtUtc,
            job.Logs.Select(l => l.CreatedAtUtc)
                .Concat(job.Events.Select(e => e.CreatedAtUtc))
                .DefaultIfEmpty(job.CompletedAtUtc ?? job.ClaimedAtUtc ?? job.CreatedAtUtc)
                .Max(),
            job.CompletedAtUtc.HasValue ? (job.CompletedAtUtc.Value - job.CreatedAtUtc).TotalSeconds : null,
            job.Events.OrderByDescending(e => e.SequenceNumber).Select(e => e.Message).FirstOrDefault(),
            job.OutcomeSummary,
            job.Events.OrderBy(e => e.SequenceNumber)
                .Select(e => new JobEventResponse(e.Id, e.SequenceNumber, e.State, e.Message, e.CreatedAtUtc))
                .ToArray(),
            job.Logs.OrderBy(l => l.CreatedAtUtc)
                .Select(l => new JobLogResponse(l.Id, l.Level, l.Source, l.Message, l.PayloadJson, l.CreatedAtUtc))
                .ToArray());
}
