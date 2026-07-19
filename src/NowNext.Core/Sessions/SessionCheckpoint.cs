using NowNext.Core.Domain;

namespace NowNext.Core.Sessions;

public sealed record SessionCheckpoint
{
    public SessionCheckpoint(
        SessionId id,
        TaskId taskId,
        TimingMode timingMode,
        TimeSpan originalPlannedDuration,
        TimeSpan approvedLimit,
        SessionCheckpointState state,
        TimeSpan committedActiveDuration,
        TimeSpan landingDuration,
        TimeSpan breakDuration,
        DateTimeOffset checkpointedAtUtc,
        ActiveSessionPhase? resumePhase = null,
        SessionBoundary? boundary = null,
        ActiveSessionPhase? recoveryPhase = null,
        SessionOutcome? priorOutcome = null,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? completedAtUtc = null,
        DateTimeOffset? parkedAtUtc = null,
        DateTimeOffset? dayClosedAtUtc = null,
        string? parkedNextPhysicalAction = null,
        DateTimeOffset? abandonedAtUtc = null,
        BreakPlan? breakPlan = null)
    {
        ValidateIdentityAndDurations(
            id,
            taskId,
            timingMode,
            originalPlannedDuration,
            approvedLimit,
            committedActiveDuration,
            landingDuration,
            breakDuration);
        ValidateEnums(state, resumePhase, boundary, recoveryPhase, priorOutcome);

        string? parkedAction = ValidateStateData(
            state,
            resumePhase,
            boundary,
            recoveryPhase,
            priorOutcome,
            completedAtUtc,
            parkedAtUtc,
            dayClosedAtUtc,
            parkedNextPhysicalAction,
            abandonedAtUtc,
            breakPlan);
        ValidateBreakDuration(state, breakDuration, breakPlan);

        Id = id;
        TaskId = taskId;
        TimingMode = timingMode;
        OriginalPlannedDuration = originalPlannedDuration;
        ApprovedLimit = approvedLimit;
        State = state;
        CommittedActiveDuration = committedActiveDuration;
        LandingDuration = landingDuration;
        BreakDuration = breakDuration;
        CheckpointedAtUtc = checkpointedAtUtc.ToUniversalTime();
        ResumePhase = resumePhase;
        Boundary = boundary;
        RecoveryPhase = recoveryPhase;
        PriorOutcome = priorOutcome;
        StartedAtUtc = startedAtUtc?.ToUniversalTime();
        CompletedAtUtc = completedAtUtc?.ToUniversalTime();
        ParkedAtUtc = parkedAtUtc?.ToUniversalTime();
        DayClosedAtUtc = dayClosedAtUtc?.ToUniversalTime();
        ParkedNextPhysicalAction = parkedAction;
        AbandonedAtUtc = abandonedAtUtc?.ToUniversalTime();
        BreakPlan = breakPlan;
    }

    public SessionId Id { get; }

    public TaskId TaskId { get; }

    public TimingMode TimingMode { get; }

    public TimeSpan OriginalPlannedDuration { get; }

    public TimeSpan ApprovedLimit { get; }

    public SessionCheckpointState State { get; }

    public TimeSpan CommittedActiveDuration { get; }

    public TimeSpan LandingDuration { get; }

    public TimeSpan BreakDuration { get; }

    public DateTimeOffset CheckpointedAtUtc { get; }

    public ActiveSessionPhase? ResumePhase { get; }

    public SessionBoundary? Boundary { get; }

    public ActiveSessionPhase? RecoveryPhase { get; }

    public SessionOutcome? PriorOutcome { get; }

    public DateTimeOffset? StartedAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public DateTimeOffset? ParkedAtUtc { get; }

    public DateTimeOffset? DayClosedAtUtc { get; }

    public string? ParkedNextPhysicalAction { get; }

    public DateTimeOffset? AbandonedAtUtc { get; }

    public BreakPlan? BreakPlan { get; }

    private static void ValidateIdentityAndDurations(
        SessionId id,
        TaskId taskId,
        TimingMode timingMode,
        TimeSpan originalPlannedDuration,
        TimeSpan approvedLimit,
        TimeSpan committedActiveDuration,
        TimeSpan landingDuration,
        TimeSpan breakDuration)
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

        if (originalPlannedDuration <= TimeSpan.Zero || approvedLimit <= TimeSpan.Zero)
        {
            throw new ArgumentException("Session limits must be positive.");
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
            throw new ArgumentException("Stored Landing duration is invalid.", nameof(landingDuration));
        }
    }

    private static void ValidateEnums(
        SessionCheckpointState state,
        ActiveSessionPhase? resumePhase,
        SessionBoundary? boundary,
        ActiveSessionPhase? recoveryPhase,
        SessionOutcome? priorOutcome)
    {
        if (!Enum.IsDefined(state)
            || (resumePhase is not null && !Enum.IsDefined(resumePhase.Value))
            || (boundary is not null && !Enum.IsDefined(boundary.Value))
            || (recoveryPhase is not null && !Enum.IsDefined(recoveryPhase.Value))
            || (priorOutcome is not null && !Enum.IsDefined(priorOutcome.Value)))
        {
            throw new ArgumentException("Session checkpoint contains an undefined enum value.");
        }
    }

    private static string? ValidateStateData(
        SessionCheckpointState state,
        ActiveSessionPhase? resumePhase,
        SessionBoundary? boundary,
        ActiveSessionPhase? recoveryPhase,
        SessionOutcome? priorOutcome,
        DateTimeOffset? completedAtUtc,
        DateTimeOffset? parkedAtUtc,
        DateTimeOffset? dayClosedAtUtc,
        string? parkedNextPhysicalAction,
        DateTimeOffset? abandonedAtUtc,
        BreakPlan? breakPlan)
    {
        bool validShape = state switch
        {
            SessionCheckpointState.Ready =>
                resumePhase is null && boundary is null && recoveryPhase is null
                && priorOutcome is null && completedAtUtc is null && parkedAtUtc is null
                && dayClosedAtUtc is null && parkedNextPhysicalAction is null
                && abandonedAtUtc is null && breakPlan is null,
            SessionCheckpointState.Paused =>
                resumePhase is ActiveSessionPhase.Focusing or ActiveSessionPhase.Overtime
                && boundary is null && recoveryPhase is null && priorOutcome is null
                && completedAtUtc is null && parkedAtUtc is null && dayClosedAtUtc is null
                && parkedNextPhysicalAction is null && abandonedAtUtc is null
                && breakPlan is null,
            SessionCheckpointState.LimitReached =>
                resumePhase is null && boundary is not null && recoveryPhase is null
                && priorOutcome is null && completedAtUtc is null && parkedAtUtc is null
                && dayClosedAtUtc is null && parkedNextPhysicalAction is null
                && abandonedAtUtc is null && breakPlan is null,
            SessionCheckpointState.Completed =>
                resumePhase is null && boundary is null && recoveryPhase is null
                && priorOutcome is null && completedAtUtc is not null && parkedAtUtc is null
                && dayClosedAtUtc is null && parkedNextPhysicalAction is null
                && abandonedAtUtc is null && breakPlan is null,
            SessionCheckpointState.Parked =>
                resumePhase is null && boundary is null && recoveryPhase is null
                && priorOutcome is null && completedAtUtc is null && parkedAtUtc is not null
                && dayClosedAtUtc is null && !string.IsNullOrWhiteSpace(parkedNextPhysicalAction)
                && abandonedAtUtc is null && breakPlan is null,
            SessionCheckpointState.BreakCompleted =>
                resumePhase is null && boundary is null && recoveryPhase is null
                && dayClosedAtUtc is null && abandonedAtUtc is null && breakPlan is not null
                && ValidateOutcome(
                    priorOutcome,
                    completedAtUtc,
                    parkedAtUtc,
                    parkedNextPhysicalAction),
            SessionCheckpointState.Abandoned =>
                resumePhase is null && boundary is null && recoveryPhase is null
                && priorOutcome is null && completedAtUtc is null && parkedAtUtc is null
                && dayClosedAtUtc is null && parkedNextPhysicalAction is null
                && abandonedAtUtc is not null && breakPlan is null,
            SessionCheckpointState.RecoveryRequired =>
                resumePhase is null && boundary is null && recoveryPhase is not null
                && dayClosedAtUtc is null && abandonedAtUtc is null && ValidateRecoveryOutcome(
                    recoveryPhase.Value,
                    priorOutcome,
                    completedAtUtc,
                    parkedAtUtc,
                    parkedNextPhysicalAction,
                    breakPlan),
            SessionCheckpointState.DayClosed =>
                resumePhase is null && boundary is null && recoveryPhase is null
                && dayClosedAtUtc is not null && abandonedAtUtc is null && breakPlan is null
                && ValidateDayClosedOutcome(
                    priorOutcome,
                    completedAtUtc,
                    parkedAtUtc,
                    parkedNextPhysicalAction),
            _ => false,
        };

        if (!validShape)
        {
            throw new ArgumentException(
                $"Checkpoint fields are inconsistent with state '{state}'.",
                nameof(state));
        }

        return parkedNextPhysicalAction?.Trim();
    }

    private static bool ValidateRecoveryOutcome(
        ActiveSessionPhase recoveryPhase,
        SessionOutcome? priorOutcome,
        DateTimeOffset? completedAtUtc,
        DateTimeOffset? parkedAtUtc,
        string? parkedNextPhysicalAction,
        BreakPlan? breakPlan)
    {
        if (recoveryPhase != ActiveSessionPhase.Break)
        {
            return priorOutcome is null && completedAtUtc is null && parkedAtUtc is null
                && parkedNextPhysicalAction is null && breakPlan is null;
        }

        return breakPlan is not null
            && ValidateOutcome(
                priorOutcome,
                completedAtUtc,
                parkedAtUtc,
                parkedNextPhysicalAction);
    }

    private static bool ValidateDayClosedOutcome(
        SessionOutcome? priorOutcome,
        DateTimeOffset? completedAtUtc,
        DateTimeOffset? parkedAtUtc,
        string? parkedNextPhysicalAction)
    {
        return priorOutcome is null
            ? completedAtUtc is null && parkedAtUtc is null && parkedNextPhysicalAction is null
            : ValidateOutcome(priorOutcome, completedAtUtc, parkedAtUtc, parkedNextPhysicalAction);
    }

    private static bool ValidateOutcome(
        SessionOutcome? priorOutcome,
        DateTimeOffset? completedAtUtc,
        DateTimeOffset? parkedAtUtc,
        string? parkedNextPhysicalAction)
    {
        return priorOutcome switch
        {
            SessionOutcome.Completed =>
                completedAtUtc is not null && parkedAtUtc is null
                && parkedNextPhysicalAction is null,
            SessionOutcome.Parked =>
                completedAtUtc is null && parkedAtUtc is not null
                && !string.IsNullOrWhiteSpace(parkedNextPhysicalAction),
            _ => false,
        };
    }

    private static void ValidateBreakDuration(
        SessionCheckpointState state,
        TimeSpan breakDuration,
        BreakPlan? breakPlan)
    {
        if (breakPlan is null)
        {
            return;
        }

        if (breakDuration > breakPlan.Duration)
        {
            throw new ArgumentException(
                "Stored Break duration must not exceed its approved limit.",
                nameof(breakDuration));
        }

        if (state == SessionCheckpointState.BreakCompleted
            && breakDuration != breakPlan.Duration)
        {
            throw new ArgumentException(
                "A completed Break must equal its approved limit.",
                nameof(breakDuration));
        }
    }
}
