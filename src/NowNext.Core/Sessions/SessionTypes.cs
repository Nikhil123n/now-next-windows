using System.Globalization;

namespace NowNext.Core.Sessions;

public readonly record struct SessionId
{
    public SessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Session ID must not be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString()
    {
        return Value.ToString("D", CultureInfo.InvariantCulture);
    }
}

public enum SessionStateKind
{
    Ready,
    Focusing,
    Paused,
    LimitReached,
    Overtime,
    Landing,
    Break,
    BreakCompleted,
    Completed,
    Parked,
    Abandoned,
    RecoveryRequired,
    DayClosed,
}

public enum ActiveSessionPhase
{
    Focusing,
    Overtime,
    Landing,
    Break,
}

public enum SessionBoundary
{
    FocusLimit,
    LandingLimit,
}

public enum SessionOutcome
{
    Completed,
    Parked,
}

public enum SessionCheckpointState
{
    Ready,
    Paused,
    LimitReached,
    Completed,
    Parked,
    BreakCompleted,
    Abandoned,
    RecoveryRequired,
    DayClosed,
}

public enum SessionSignal
{
    None,
    FocusLimitReached,
    LandingLimitReached,
    BreakLimitReached,
}

public abstract record SessionState
{
    private protected SessionState()
    {
    }

    public abstract SessionStateKind Kind { get; }
}

public sealed record ReadySessionState : SessionState
{
    public override SessionStateKind Kind => SessionStateKind.Ready;
}

public sealed record FocusingSessionState(long SegmentStartedTimestamp) : SessionState
{
    public override SessionStateKind Kind => SessionStateKind.Focusing;
}

public sealed record PausedSessionState : SessionState
{
    public PausedSessionState(ActiveSessionPhase resumePhase)
    {
        if (resumePhase is not (ActiveSessionPhase.Focusing or ActiveSessionPhase.Overtime))
        {
            throw new ArgumentException(
                "A paused session can resume only focusing or overtime.",
                nameof(resumePhase));
        }

        ResumePhase = resumePhase;
    }

    public ActiveSessionPhase ResumePhase { get; }

    public override SessionStateKind Kind => SessionStateKind.Paused;
}

public sealed record LimitReachedSessionState(SessionBoundary Boundary) : SessionState
{
    public override SessionStateKind Kind => SessionStateKind.LimitReached;
}

public sealed record OvertimeSessionState(long SegmentStartedTimestamp) : SessionState
{
    public override SessionStateKind Kind => SessionStateKind.Overtime;
}

public sealed record LandingSessionState(long SegmentStartedTimestamp) : SessionState
{
    public override SessionStateKind Kind => SessionStateKind.Landing;
}

public sealed record BreakSessionState : SessionState
{
    public BreakSessionState(
        long segmentStartedTimestamp,
        BreakPlan plan,
        SessionOutcome priorOutcome,
        DateTimeOffset outcomeAtUtc,
        string? parkedNextPhysicalAction)
    {
        if (!Enum.IsDefined(priorOutcome))
        {
            throw new ArgumentException("Session outcome is not defined.", nameof(priorOutcome));
        }

        SegmentStartedTimestamp = segmentStartedTimestamp;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        PriorOutcome = priorOutcome;
        OutcomeAtUtc = outcomeAtUtc.ToUniversalTime();
        ParkedNextPhysicalAction = ValidateParkedAction(
            priorOutcome,
            parkedNextPhysicalAction,
            nameof(parkedNextPhysicalAction));
    }

    public long SegmentStartedTimestamp { get; }

    public BreakPlan Plan { get; }

    public SessionOutcome PriorOutcome { get; }

    public DateTimeOffset OutcomeAtUtc { get; }

    public string? ParkedNextPhysicalAction { get; }

    public override SessionStateKind Kind => SessionStateKind.Break;

    internal static string? ValidateParkedAction(
        SessionOutcome outcome,
        string? value,
        string parameterName)
    {
        if (outcome == SessionOutcome.Completed)
        {
            if (value is not null)
            {
                throw new ArgumentException(
                    "A completed outcome must not have a parked next action.",
                    parameterName);
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A parked outcome requires a next physical action.",
                parameterName);
        }

        return value.Trim();
    }
}

public sealed record BreakCompletedSessionState : SessionState
{
    public BreakCompletedSessionState(
        BreakPlan plan,
        SessionOutcome priorOutcome,
        DateTimeOffset outcomeAtUtc,
        string? parkedNextPhysicalAction)
    {
        if (!Enum.IsDefined(priorOutcome))
        {
            throw new ArgumentException("Session outcome is not defined.", nameof(priorOutcome));
        }

        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        PriorOutcome = priorOutcome;
        OutcomeAtUtc = outcomeAtUtc.ToUniversalTime();
        ParkedNextPhysicalAction = BreakSessionState.ValidateParkedAction(
            priorOutcome,
            parkedNextPhysicalAction,
            nameof(parkedNextPhysicalAction));
    }

    public BreakPlan Plan { get; }

    public SessionOutcome PriorOutcome { get; }

    public DateTimeOffset OutcomeAtUtc { get; }

    public string? ParkedNextPhysicalAction { get; }

    public override SessionStateKind Kind => SessionStateKind.BreakCompleted;
}

public sealed record CompletedSessionState(DateTimeOffset CompletedAtUtc) : SessionState
{
    public DateTimeOffset CompletedAtUtc { get; } = CompletedAtUtc.ToUniversalTime();

    public override SessionStateKind Kind => SessionStateKind.Completed;
}

public sealed record ParkedSessionState : SessionState
{
    public ParkedSessionState(DateTimeOffset parkedAtUtc, string nextPhysicalAction)
    {
        ParkedAtUtc = parkedAtUtc.ToUniversalTime();
        NextPhysicalAction = string.IsNullOrWhiteSpace(nextPhysicalAction)
            ? throw new ArgumentException(
                "Parking requires a next physical action.",
                nameof(nextPhysicalAction))
            : nextPhysicalAction.Trim();
    }

    public DateTimeOffset ParkedAtUtc { get; }

    public string NextPhysicalAction { get; }

    public override SessionStateKind Kind => SessionStateKind.Parked;
}

public sealed record AbandonedSessionState(DateTimeOffset AbandonedAtUtc) : SessionState
{
    public DateTimeOffset AbandonedAtUtc { get; } = AbandonedAtUtc.ToUniversalTime();

    public override SessionStateKind Kind => SessionStateKind.Abandoned;
}

public sealed record RecoveryRequiredSessionState : SessionState
{
    public RecoveryRequiredSessionState(
        ActiveSessionPhase interruptedPhase,
        DateTimeOffset checkpointedAtUtc,
        DateTimeOffset detectedAtUtc,
        SessionOutcome? priorOutcome = null,
        DateTimeOffset? outcomeAtUtc = null,
        string? parkedNextPhysicalAction = null,
        BreakPlan? breakPlan = null)
    {
        if (!Enum.IsDefined(interruptedPhase))
        {
            throw new ArgumentException("Interrupted phase is not defined.", nameof(interruptedPhase));
        }

        if (interruptedPhase == ActiveSessionPhase.Break)
        {
            if (priorOutcome is null || outcomeAtUtc is null || breakPlan is null)
            {
                throw new ArgumentException(
                    "Break recovery requires its plan, prior outcome, and timestamp.",
                    nameof(priorOutcome));
            }

            ParkedNextPhysicalAction = BreakSessionState.ValidateParkedAction(
                priorOutcome.Value,
                parkedNextPhysicalAction,
                nameof(parkedNextPhysicalAction));
        }
        else if (priorOutcome is not null
            || outcomeAtUtc is not null
            || parkedNextPhysicalAction is not null
            || breakPlan is not null)
        {
            throw new ArgumentException(
                "Only Break recovery can contain a prior outcome.",
                nameof(priorOutcome));
        }

        InterruptedPhase = interruptedPhase;
        CheckpointedAtUtc = checkpointedAtUtc.ToUniversalTime();
        DetectedAtUtc = detectedAtUtc.ToUniversalTime();
        PriorOutcome = priorOutcome;
        OutcomeAtUtc = outcomeAtUtc?.ToUniversalTime();
        BreakPlan = breakPlan;
    }

    public ActiveSessionPhase InterruptedPhase { get; }

    public DateTimeOffset CheckpointedAtUtc { get; }

    public DateTimeOffset DetectedAtUtc { get; }

    public SessionOutcome? PriorOutcome { get; }

    public DateTimeOffset? OutcomeAtUtc { get; }

    public string? ParkedNextPhysicalAction { get; }

    public BreakPlan? BreakPlan { get; }

    public override SessionStateKind Kind => SessionStateKind.RecoveryRequired;
}

public sealed record DayClosedSessionState : SessionState
{
    public DayClosedSessionState(
        DateTimeOffset closedAtUtc,
        SessionOutcome? priorOutcome = null,
        DateTimeOffset? outcomeAtUtc = null,
        string? parkedNextPhysicalAction = null)
    {
        if (priorOutcome is null)
        {
            if (outcomeAtUtc is not null || parkedNextPhysicalAction is not null)
            {
                throw new ArgumentException(
                    "A day closed without an outcome cannot contain outcome data.",
                    nameof(priorOutcome));
            }
        }
        else
        {
            if (outcomeAtUtc is null)
            {
                throw new ArgumentException(
                    "A day-closed outcome requires its timestamp.",
                    nameof(outcomeAtUtc));
            }

            ParkedNextPhysicalAction = BreakSessionState.ValidateParkedAction(
                priorOutcome.Value,
                parkedNextPhysicalAction,
                nameof(parkedNextPhysicalAction));
        }

        ClosedAtUtc = closedAtUtc.ToUniversalTime();
        PriorOutcome = priorOutcome;
        OutcomeAtUtc = outcomeAtUtc?.ToUniversalTime();
    }

    public DateTimeOffset ClosedAtUtc { get; }

    public SessionOutcome? PriorOutcome { get; }

    public DateTimeOffset? OutcomeAtUtc { get; }

    public string? ParkedNextPhysicalAction { get; }

    public override SessionStateKind Kind => SessionStateKind.DayClosed;
}
