namespace Gamarr.Agent.Models;

public sealed record RegisterMachineRequest(
    string StableKey,
    string Name,
    string Hostname,
    string OperatingSystem,
    ArchitectureKind Architecture,
    string AgentVersion,
    IReadOnlyCollection<string> Capabilities);

public sealed record HeartbeatRequest(
    MachineStatus Status,
    string AgentVersion,
    IReadOnlyCollection<string> Capabilities);

public sealed record ClaimJobRequest(Guid MachineId);

public sealed record JobLogRequest(LogLevelKind Level, string Source, string Message, string? PayloadJson);

public sealed record JobEventRequest(
    int SequenceNumber,
    JobState State,
    string Message,
    IReadOnlyCollection<JobLogRequest> Logs,
    string? LaunchExecutablePath = null,
    bool? DetectedInstalled = null,
    string? ValidationMessage = null,
    string? InstallLocation = null,
    string? ResolvedUninstallCommand = null);

public sealed class MachineResponse
{
    public Guid Id { get; set; }
    public string StableKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class NextJobResponse
{
    public Guid Id { get; set; }
    public Guid PackageId { get; set; }
    public JobActionType ActionType { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string PackageVersionLabel { get; set; } = string.Empty;
    public string SupportedOs { get; set; } = string.Empty;
    public ArchitectureKind Architecture { get; set; }
    public InstallScriptKind InstallScriptKind { get; set; }
    public string InstallScriptPath { get; set; } = string.Empty;
    public string? UninstallScriptPath { get; set; }
    public string? UninstallArguments { get; set; }
    public string ManifestFormatVersion { get; set; } = string.Empty;
    public string ManifestJson { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string InstallStrategy { get; set; } = string.Empty;
    public string InstallerFamily { get; set; } = string.Empty;
    public string? InstallerPath { get; set; }
    public string? SilentArguments { get; set; }
    public string InstallDiagnostics { get; set; } = string.Empty;
    public string? LaunchExecutablePath { get; set; }
    public IReadOnlyCollection<PackageMediaResponse> Media { get; set; } = [];
    public IReadOnlyCollection<DetectionRuleResponse> DetectionRules { get; set; } = [];
    public IReadOnlyCollection<PrerequisiteResponse> Prerequisites { get; set; } = [];
}

public sealed class PackageMediaResponse
{
    public Guid Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int? DiscNumber { get; set; }
    public string? EntrypointHint { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string ScratchPolicy { get; set; } = string.Empty;
}

public sealed class DetectionRuleResponse
{
    public Guid Id { get; set; }
    public string RuleType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class PrerequisiteResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class NextMountResponse
{
    public Guid MountId { get; set; }
    public string IsoPath { get; set; } = string.Empty;
}

public sealed record ReportMountResultRequest(string? DriveLetter, string? Error);
