using Gamarr.Domain.Enums;

namespace Gamarr.Domain.Entities;

public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public Package? Package { get; set; }
    public Guid PackageVersionId { get; set; }
    public PackageVersion? PackageVersion { get; set; }
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public JobActionType ActionType { get; set; } = JobActionType.Install;
    public JobState State { get; set; } = JobState.Queued;
    public string RequestedBy { get; set; } = "local-admin";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? OutcomeSummary { get; set; }
    public ICollection<JobEvent> Events { get; set; } = new List<JobEvent>();
    public ICollection<PackageActionLog> Logs { get; set; } = new List<PackageActionLog>();
}
