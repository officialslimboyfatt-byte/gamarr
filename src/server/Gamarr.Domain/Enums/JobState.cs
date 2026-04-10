namespace Gamarr.Domain.Enums;

public enum JobState
{
    Queued = 1,
    Assigned = 2,
    Preparing = 3,
    Mounting = 4,
    Installing = 5,
    Validating = 6,
    Completed = 7,
    Failed = 8,
    Cancelled = 9
}
