namespace Gamarr.Agent.Models;

public enum ArchitectureKind
{
    X86,
    X64
}

public enum MachineStatus
{
    Unknown,
    Online,
    Offline,
    Busy
}

public enum JobState
{
    Queued,
    Assigned,
    Preparing,
    Mounting,
    Installing,
    Validating,
    Completed,
    Failed,
    Cancelled
}

public enum LogLevelKind
{
    Trace,
    Information,
    Warning,
    Error
}

public enum InstallScriptKind
{
    MockRecipe,
    PowerShell
}

public enum JobActionType
{
    Install,
    Launch,
    Validate,
    Uninstall
}
