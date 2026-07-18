using NowNext.Core.Domain;

namespace NowNext.Core.Sessions;

public sealed record FocusSession
{
    internal FocusSession(
        SessionId id,
        TaskId taskId,
        TimingMode timingMode,
        TimeSpan originalPlannedDuration,
        TimeSpan approvedLimit,
        TimeSpan committedActiveDuration,
        TimeSpan landingDuration,
        TimeSpan breakDuration,
        DateTimeOffset? startedAtUtc,
        SessionState state)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("Session ID must not be empty.", nameof(id));
        }

        if (taskId.Value == Guid.Empty)
        {
            throw new ArgumentException("Task ID must not be empty.", nameof(taskId));
        }

        if (!Enum.IsDefined(timingMode))
        {
            throw new ArgumentException("Timing mode is not defined.", nameof(timingMode));
        }

        if (originalPlannedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Original planned duration must be positive.",
                nameof(originalPlannedDuration));
        }

        if (approvedLimit <= TimeSpan.Zero)
        {
            throw new ArgumentException("Approved limit must be positive.", nameof(approvedLimit));
        }

        if (approvedLimit < originalPlannedDuration)
        {
            throw new ArgumentException(
                "Approved limit must not be shorter than the original planned duration.",
                nameof(approvedLimit));
        }

        if (committedActiveDuration < TimeSpan.Zero
            || landingDuration < TimeSpan.Zero
            || breakDuration < TimeSpan.Zero)
        {
            throw new ArgumentException("Committed durations must not be negative.");
        }

        if (landingDuration > committedActiveDuration
            || landingDuration > FocusSessionMachine.LandingLimit)
        {
            throw new ArgumentException(
                "Landing duration must be within committed active duration and its limit.",
                nameof(landingDuration));
        }

        Id = id;
        TaskId = taskId;
        TimingMode = timingMode;
        OriginalPlannedDuration = originalPlannedDuration;
        ApprovedLimit = approvedLimit;
        CommittedActiveDuration = committedActiveDuration;
        LandingDuration = landingDuration;
        BreakDuration = breakDuration;
        StartedAtUtc = startedAtUtc?.ToUniversalTime();
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public SessionId Id { get; }

    public TaskId TaskId { get; }

    public TimingMode TimingMode { get; }

    public TimeSpan OriginalPlannedDuration { get; }

    public TimeSpan ApprovedLimit { get; }

    public TimeSpan CommittedActiveDuration { get; }

    public TimeSpan LandingDuration { get; }

    public TimeSpan BreakDuration { get; }

    public DateTimeOffset? StartedAtUtc { get; }

    public SessionState State { get; }
}

public sealed record SessionTransition(FocusSession Session, SessionSignal Signal);
