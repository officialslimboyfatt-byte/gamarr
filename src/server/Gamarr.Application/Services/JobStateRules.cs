using Gamarr.Domain.Enums;

namespace Gamarr.Application.Services;

public static class JobStateRules
{
    private static readonly Dictionary<JobState, JobState[]> AllowedTransitions = new()
    {
        [JobState.Queued] = [JobState.Assigned, JobState.Cancelled],
        [JobState.Assigned] = [JobState.Preparing, JobState.Failed, JobState.Cancelled],
        [JobState.Preparing] = [JobState.Mounting, JobState.Installing, JobState.Validating, JobState.Completed, JobState.Failed, JobState.Cancelled],
        [JobState.Mounting] = [JobState.Installing, JobState.Failed, JobState.Cancelled],
        [JobState.Installing] = [JobState.Validating, JobState.Failed, JobState.Cancelled],
        [JobState.Validating] = [JobState.Completed, JobState.Failed, JobState.Cancelled],
        [JobState.Completed] = [],
        [JobState.Failed] = [],
        [JobState.Cancelled] = []
    };

    public static bool CanTransition(JobState from, JobState to) =>
        AllowedTransitions.TryGetValue(from, out var next) && next.Contains(to);
}
