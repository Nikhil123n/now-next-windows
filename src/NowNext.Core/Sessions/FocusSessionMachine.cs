using NowNext.Core.Domain;

namespace NowNext.Core.Sessions;

public static class FocusSessionMachine
{
    public static readonly TimeSpan LandingLimit = TimeSpan.FromMinutes(5);

    public static FocusSession Create(
        SessionId id,
        TaskId taskId,
        TimingMode timingMode,
        TimeSpan plannedDuration)
    {
        return new FocusSession(
            id,
            taskId,
            timingMode,
            plannedDuration,
            plannedDuration,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            null,
            new ReadySessionState());
    }

    public static SessionTransition Apply(
        FocusSession session,
        SessionCommand command,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(timeProvider);

        long timestamp = timeProvider.GetTimestamp();
        DateTimeOffset utcNow = timeProvider.GetUtcNow().ToUniversalTime();

        return command switch
        {
            RefreshSession => Refresh(session, timeProvider, timestamp),
            StartSession => Start(session, timestamp, utcNow),
            PauseSession => Pause(session, timeProvider, timestamp),
            ResumeSession => Resume(session, timestamp),
            ContinueOvertime => StartOvertime(session, timestamp),
            BeginLanding => StartLanding(session, timeProvider, timestamp),
            ExtendSession extension => Extend(session, extension, timeProvider, timestamp),
            CompleteSession => Complete(session, timeProvider, timestamp, utcNow),
            ParkSession park => Park(session, park, timeProvider, timestamp, utcNow),
            BeginBreak => StartBreak(session, timestamp),
            EndBreak => FinishBreak(session, timeProvider, timestamp),
            InterruptSession => Interrupt(session, timeProvider, timestamp, utcNow),
            ResumeWithoutAwayTime => Recover(session, TimeSpan.Zero, timestamp),
            ResumeIncludingAwayTime include => Recover(session, include.Duration, timestamp),
            CloseDay => Close(session, timeProvider, timestamp, utcNow),
            _ => throw new InvalidOperationException(
                $"Command '{command.GetType().Name}' is not supported."),
        };
    }

    public static SessionCheckpoint CreateCheckpoint(
        FocusSession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        long timestamp = timeProvider.GetTimestamp();
        DateTimeOffset utcNow = timeProvider.GetUtcNow().ToUniversalTime();
        FocusSession current = Refresh(session, timeProvider, timestamp).Session;

        return current.State switch
        {
            ReadySessionState => CreateCheckpoint(current, SessionCheckpointState.Ready, utcNow),
            FocusingSessionState => CreateRecoveryCheckpoint(
                current,
                ActiveSessionPhase.Focusing,
                utcNow),
            PausedSessionState paused => CreateCheckpoint(
                current,
                SessionCheckpointState.Paused,
                utcNow,
                resumePhase: paused.ResumePhase),
            LimitReachedSessionState limit => CreateCheckpoint(
                current,
                SessionCheckpointState.LimitReached,
                utcNow,
                boundary: limit.Boundary),
            OvertimeSessionState => CreateRecoveryCheckpoint(
                current,
                ActiveSessionPhase.Overtime,
                utcNow),
            LandingSessionState => CreateRecoveryCheckpoint(
                current,
                ActiveSessionPhase.Landing,
                utcNow),
            BreakSessionState @break => CreateRecoveryCheckpoint(
                current,
                ActiveSessionPhase.Break,
                utcNow,
                @break.PriorOutcome,
                @break.OutcomeAtUtc,
                @break.ParkedNextPhysicalAction),
            CompletedSessionState completed => CreateCheckpoint(
                current,
                SessionCheckpointState.Completed,
                utcNow,
                completedAtUtc: completed.CompletedAtUtc),
            ParkedSessionState parked => CreateCheckpoint(
                current,
                SessionCheckpointState.Parked,
                utcNow,
                parkedAtUtc: parked.ParkedAtUtc,
                parkedNextPhysicalAction: parked.NextPhysicalAction),
            RecoveryRequiredSessionState recovery => CreateRecoveryCheckpoint(
                current,
                recovery.InterruptedPhase,
                recovery.CheckpointedAtUtc,
                recovery.PriorOutcome,
                recovery.OutcomeAtUtc,
                recovery.ParkedNextPhysicalAction),
            DayClosedSessionState closed => CreateCheckpoint(
                current,
                SessionCheckpointState.DayClosed,
                utcNow,
                priorOutcome: closed.PriorOutcome,
                completedAtUtc: closed.PriorOutcome == SessionOutcome.Completed
                    ? closed.OutcomeAtUtc
                    : null,
                parkedAtUtc: closed.PriorOutcome == SessionOutcome.Parked
                    ? closed.OutcomeAtUtc
                    : null,
                dayClosedAtUtc: closed.ClosedAtUtc,
                parkedNextPhysicalAction: closed.ParkedNextPhysicalAction),
            _ => throw new InvalidOperationException("Session state cannot be checkpointed."),
        };
    }

    public static FocusSession Restore(
        SessionCheckpoint checkpoint,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(timeProvider);

        DateTimeOffset detectedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        SessionState state = checkpoint.State switch
        {
            SessionCheckpointState.Ready => new ReadySessionState(),
            SessionCheckpointState.Paused => new PausedSessionState(
                checkpoint.ResumePhase!.Value),
            SessionCheckpointState.LimitReached => new LimitReachedSessionState(
                checkpoint.Boundary!.Value),
            SessionCheckpointState.Completed => new CompletedSessionState(
                checkpoint.CompletedAtUtc!.Value),
            SessionCheckpointState.Parked => new ParkedSessionState(
                checkpoint.ParkedAtUtc!.Value,
                checkpoint.ParkedNextPhysicalAction!),
            SessionCheckpointState.RecoveryRequired => new RecoveryRequiredSessionState(
                checkpoint.RecoveryPhase!.Value,
                checkpoint.CheckpointedAtUtc,
                detectedAtUtc,
                checkpoint.PriorOutcome,
                GetOutcomeTimestamp(checkpoint),
                checkpoint.ParkedNextPhysicalAction),
            SessionCheckpointState.DayClosed => new DayClosedSessionState(
                checkpoint.DayClosedAtUtc!.Value,
                checkpoint.PriorOutcome,
                GetOutcomeTimestamp(checkpoint),
                checkpoint.ParkedNextPhysicalAction),
            _ => throw new InvalidDataException("Stored session state is not supported."),
        };

        return new FocusSession(
            checkpoint.Id,
            checkpoint.TaskId,
            checkpoint.TimingMode,
            checkpoint.OriginalPlannedDuration,
            checkpoint.ApprovedLimit,
            checkpoint.CommittedActiveDuration,
            checkpoint.LandingDuration,
            checkpoint.BreakDuration,
            checkpoint.StartedAtUtc,
            state);
    }

    public static SessionView CreateView(FocusSession session, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        FocusSession current = Refresh(
            session,
            timeProvider,
            timeProvider.GetTimestamp()).Session;
        TimerReading timer = current.State switch
        {
            FocusingSessionState or PausedSessionState => CreateFocusReading(current),
            LimitReachedSessionState { Boundary: SessionBoundary.FocusLimit } =>
                CreateFocusReading(current),
            OvertimeSessionState => new OvertimeTimerReading(
                current.CommittedActiveDuration - current.ApprovedLimit),
            LandingSessionState => new LandingTimerReading(
                current.LandingDuration,
                LandingLimit),
            LimitReachedSessionState { Boundary: SessionBoundary.LandingLimit } =>
                new LandingTimerReading(LandingLimit, LandingLimit),
            BreakSessionState => new BreakTimerReading(current.BreakDuration),
            RecoveryRequiredSessionState { InterruptedPhase: ActiveSessionPhase.Break } =>
                new BreakTimerReading(current.BreakDuration),
            RecoveryRequiredSessionState { InterruptedPhase: ActiveSessionPhase.Landing } =>
                new LandingTimerReading(current.LandingDuration, LandingLimit),
            RecoveryRequiredSessionState => CreateFocusReading(current),
            _ => new NoTimerReading(),
        };
        return new SessionView(current.State.Kind, timer);
    }

    private static SessionTransition Start(
        FocusSession session,
        long timestamp,
        DateTimeOffset utcNow)
    {
        RequireState<ReadySessionState>(session, nameof(StartSession));
        return Transition(
            With(session, new FocusingSessionState(timestamp), startedAtUtc: utcNow));
    }

    private static SessionTransition Pause(
        FocusSession session,
        TimeProvider timeProvider,
        long timestamp)
    {
        if (session.State is not (FocusingSessionState or OvertimeSessionState))
        {
            return Invalid(session, nameof(PauseSession));
        }

        SessionTransition refreshed = Refresh(session, timeProvider, timestamp);
        return refreshed.Session.State switch
        {
            FocusingSessionState => Transition(
                With(refreshed.Session, new PausedSessionState(ActiveSessionPhase.Focusing)),
                refreshed.Signal),
            OvertimeSessionState => Transition(
                With(refreshed.Session, new PausedSessionState(ActiveSessionPhase.Overtime)),
                refreshed.Signal),
            LimitReachedSessionState => refreshed,
            _ => throw new InvalidOperationException("Pause normalization produced an invalid state."),
        };
    }

    private static SessionTransition Resume(FocusSession session, long timestamp)
    {
        PausedSessionState paused = RequireState<PausedSessionState>(session, nameof(ResumeSession));
        SessionState state = paused.ResumePhase switch
        {
            ActiveSessionPhase.Focusing => new FocusingSessionState(timestamp),
            ActiveSessionPhase.Overtime => new OvertimeSessionState(timestamp),
            _ => throw new InvalidOperationException("Paused resume phase is invalid."),
        };
        return Transition(With(session, state));
    }

    private static SessionTransition StartOvertime(FocusSession session, long timestamp)
    {
        LimitReachedSessionState limit = RequireState<LimitReachedSessionState>(
            session,
            nameof(ContinueOvertime));
        if (limit.Boundary != SessionBoundary.FocusLimit)
        {
            return Invalid(session, nameof(ContinueOvertime));
        }

        return Transition(With(session, new OvertimeSessionState(timestamp)));
    }

    private static SessionTransition StartLanding(
        FocusSession session,
        TimeProvider timeProvider,
        long timestamp)
    {
        SessionTransition current = session.State switch
        {
            OvertimeSessionState => Refresh(session, timeProvider, timestamp),
            LimitReachedSessionState { Boundary: SessionBoundary.FocusLimit } =>
                Transition(session),
            _ => Invalid(session, nameof(BeginLanding)),
        };

        if (current.Session.LandingDuration != TimeSpan.Zero)
        {
            throw new InvalidOperationException("Landing can occur only once in a focus session.");
        }

        return Transition(
            With(current.Session, new LandingSessionState(timestamp)),
            current.Signal);
    }

    private static SessionTransition Extend(
        FocusSession session,
        ExtendSession extension,
        TimeProvider timeProvider,
        long timestamp)
    {
        if (session.State is not (
            OvertimeSessionState
            or LandingSessionState
            or LimitReachedSessionState))
        {
            return Invalid(session, nameof(ExtendSession));
        }

        SessionTransition current = Refresh(session, timeProvider, timestamp);
        long limitTicks = checked(
            current.Session.CommittedActiveDuration.Ticks + extension.Duration.Ticks);
        return Transition(
            With(
                current.Session,
                new FocusingSessionState(timestamp),
                approvedLimit: TimeSpan.FromTicks(limitTicks)),
            current.Signal);
    }

    private static SessionTransition Complete(
        FocusSession session,
        TimeProvider timeProvider,
        long timestamp,
        DateTimeOffset utcNow)
    {
        if (session.State is not (
            FocusingSessionState
            or PausedSessionState
            or LimitReachedSessionState
            or OvertimeSessionState
            or LandingSessionState
            or RecoveryRequiredSessionState))
        {
            return Invalid(session, nameof(CompleteSession));
        }

        SessionTransition current = Refresh(session, timeProvider, timestamp);
        return Transition(
            With(current.Session, new CompletedSessionState(utcNow)),
            current.Signal);
    }

    private static SessionTransition Park(
        FocusSession session,
        ParkSession park,
        TimeProvider timeProvider,
        long timestamp,
        DateTimeOffset utcNow)
    {
        if (session.State is not (
            FocusingSessionState
            or PausedSessionState
            or LimitReachedSessionState
            or OvertimeSessionState
            or LandingSessionState
            or RecoveryRequiredSessionState))
        {
            return Invalid(session, nameof(ParkSession));
        }

        SessionTransition current = Refresh(session, timeProvider, timestamp);
        return Transition(
            With(current.Session, new ParkedSessionState(utcNow, park.NextPhysicalAction)),
            current.Signal);
    }

    private static SessionTransition StartBreak(FocusSession session, long timestamp)
    {
        return session.State switch
        {
            CompletedSessionState completed => Transition(
                With(
                    session,
                    new BreakSessionState(
                        timestamp,
                        SessionOutcome.Completed,
                        completed.CompletedAtUtc,
                        null))),
            ParkedSessionState parked => Transition(
                With(
                    session,
                    new BreakSessionState(
                        timestamp,
                        SessionOutcome.Parked,
                        parked.ParkedAtUtc,
                        parked.NextPhysicalAction))),
            _ => Invalid(session, nameof(BeginBreak)),
        };
    }

    private static SessionTransition FinishBreak(
        FocusSession session,
        TimeProvider timeProvider,
        long timestamp)
    {
        RequireState<BreakSessionState>(session, nameof(EndBreak));
        FocusSession current = Refresh(session, timeProvider, timestamp).Session;
        BreakSessionState @break = (BreakSessionState)current.State;
        SessionState outcome = @break.PriorOutcome switch
        {
            SessionOutcome.Completed => new CompletedSessionState(@break.OutcomeAtUtc),
            SessionOutcome.Parked => new ParkedSessionState(
                @break.OutcomeAtUtc,
                @break.ParkedNextPhysicalAction!),
            _ => throw new InvalidOperationException("Break outcome is invalid."),
        };
        return Transition(With(current, outcome));
    }

    private static SessionTransition Interrupt(
        FocusSession session,
        TimeProvider timeProvider,
        long timestamp,
        DateTimeOffset utcNow)
    {
        if (session.State is not (
            FocusingSessionState
            or OvertimeSessionState
            or LandingSessionState
            or BreakSessionState))
        {
            return Invalid(session, nameof(InterruptSession));
        }

        SessionTransition current = Refresh(session, timeProvider, timestamp);
        SessionState recovery = current.Session.State switch
        {
            FocusingSessionState => new RecoveryRequiredSessionState(
                ActiveSessionPhase.Focusing,
                utcNow,
                utcNow),
            OvertimeSessionState => new RecoveryRequiredSessionState(
                ActiveSessionPhase.Overtime,
                utcNow,
                utcNow),
            LandingSessionState => new RecoveryRequiredSessionState(
                ActiveSessionPhase.Landing,
                utcNow,
                utcNow),
            BreakSessionState @break => new RecoveryRequiredSessionState(
                ActiveSessionPhase.Break,
                utcNow,
                utcNow,
                @break.PriorOutcome,
                @break.OutcomeAtUtc,
                @break.ParkedNextPhysicalAction),
            LimitReachedSessionState => current.Session.State,
            _ => throw new InvalidOperationException(
                "Interruption normalization produced an invalid state."),
        };
        return Transition(With(current.Session, recovery), current.Signal);
    }

    private static SessionTransition Recover(
        FocusSession session,
        TimeSpan includedAwayTime,
        long timestamp)
    {
        RecoveryRequiredSessionState recovery = RequireState<RecoveryRequiredSessionState>(
            session,
            nameof(ResumeWithoutAwayTime));
        TimeSpan observedGap = recovery.DetectedAtUtc - recovery.CheckpointedAtUtc;
        TimeSpan maximum = observedGap < TimeSpan.Zero ? TimeSpan.Zero : observedGap;
        if (includedAwayTime > maximum)
        {
            throw new InvalidOperationException(
                "Included away time exceeds the observed nonnegative recovery interval.");
        }

        return recovery.InterruptedPhase switch
        {
            ActiveSessionPhase.Focusing or ActiveSessionPhase.Overtime =>
                RecoverFocus(session, includedAwayTime, timestamp),
            ActiveSessionPhase.Landing => RecoverLanding(session, includedAwayTime, timestamp),
            ActiveSessionPhase.Break => RecoverBreak(session, recovery, includedAwayTime, timestamp),
            _ => throw new InvalidOperationException("Recovery phase is invalid."),
        };
    }

    private static SessionTransition RecoverFocus(
        FocusSession session,
        TimeSpan includedAwayTime,
        long timestamp)
    {
        TimeSpan active = Add(session.CommittedActiveDuration, includedAwayTime);
        SessionState state = active.CompareTo(session.ApprovedLimit) switch
        {
            < 0 => new FocusingSessionState(timestamp),
            0 => new LimitReachedSessionState(SessionBoundary.FocusLimit),
            > 0 => new OvertimeSessionState(timestamp),
        };
        SessionSignal signal = session.CommittedActiveDuration < session.ApprovedLimit
            && active >= session.ApprovedLimit
                ? SessionSignal.FocusLimitReached
                : SessionSignal.None;
        return Transition(
            With(session, state, committedActiveDuration: active),
            signal);
    }

    private static SessionTransition RecoverLanding(
        FocusSession session,
        TimeSpan includedAwayTime,
        long timestamp)
    {
        TimeSpan remaining = LandingLimit - session.LandingDuration;
        if (includedAwayTime > remaining)
        {
            throw new InvalidOperationException(
                "Included away time exceeds the remaining Landing duration.");
        }

        TimeSpan landing = Add(session.LandingDuration, includedAwayTime);
        TimeSpan active = Add(session.CommittedActiveDuration, includedAwayTime);
        SessionState state = landing == LandingLimit
            ? new LimitReachedSessionState(SessionBoundary.LandingLimit)
            : new LandingSessionState(timestamp);
        return Transition(
            With(
                session,
                state,
                committedActiveDuration: active,
                landingDuration: landing),
            landing == LandingLimit
                ? SessionSignal.LandingLimitReached
                : SessionSignal.None);
    }

    private static SessionTransition RecoverBreak(
        FocusSession session,
        RecoveryRequiredSessionState recovery,
        TimeSpan includedAwayTime,
        long timestamp)
    {
        return Transition(
            With(
                session,
                new BreakSessionState(
                    timestamp,
                    recovery.PriorOutcome!.Value,
                    recovery.OutcomeAtUtc!.Value,
                    recovery.ParkedNextPhysicalAction),
                breakDuration: Add(session.BreakDuration, includedAwayTime)));
    }

    private static SessionTransition Close(
        FocusSession session,
        TimeProvider timeProvider,
        long timestamp,
        DateTimeOffset utcNow)
    {
        FocusSession current = session.State is BreakSessionState
            ? Refresh(session, timeProvider, timestamp).Session
            : session;
        SessionState state = current.State switch
        {
            ReadySessionState => new DayClosedSessionState(utcNow),
            CompletedSessionState completed => new DayClosedSessionState(
                utcNow,
                SessionOutcome.Completed,
                completed.CompletedAtUtc),
            ParkedSessionState parked => new DayClosedSessionState(
                utcNow,
                SessionOutcome.Parked,
                parked.ParkedAtUtc,
                parked.NextPhysicalAction),
            BreakSessionState @break => new DayClosedSessionState(
                utcNow,
                @break.PriorOutcome,
                @break.OutcomeAtUtc,
                @break.ParkedNextPhysicalAction),
            _ => Invalid(current, nameof(CloseDay)).Session.State,
        };
        return Transition(With(current, state));
    }

    private static SessionTransition Refresh(
        FocusSession session,
        TimeProvider timeProvider,
        long timestamp)
    {
        return session.State switch
        {
            FocusingSessionState focusing => RefreshFocusing(
                session,
                timeProvider,
                focusing.SegmentStartedTimestamp,
                timestamp),
            OvertimeSessionState overtime => RefreshOvertime(
                session,
                timeProvider,
                overtime.SegmentStartedTimestamp,
                timestamp),
            LandingSessionState landing => RefreshLanding(
                session,
                timeProvider,
                landing.SegmentStartedTimestamp,
                timestamp),
            BreakSessionState @break => RefreshBreak(
                session,
                timeProvider,
                @break,
                timestamp),
            _ => Transition(session),
        };
    }

    private static SessionTransition RefreshFocusing(
        FocusSession session,
        TimeProvider timeProvider,
        long started,
        long timestamp)
    {
        TimeSpan active = Add(
            session.CommittedActiveDuration,
            GetElapsedTime(timeProvider, started, timestamp));
        SessionState state;
        SessionSignal signal = SessionSignal.None;
        if (active < session.ApprovedLimit)
        {
            state = new FocusingSessionState(timestamp);
        }
        else if (active == session.ApprovedLimit)
        {
            state = new LimitReachedSessionState(SessionBoundary.FocusLimit);
            signal = SessionSignal.FocusLimitReached;
        }
        else
        {
            state = new OvertimeSessionState(timestamp);
            signal = SessionSignal.FocusLimitReached;
        }

        return Transition(
            With(session, state, committedActiveDuration: active),
            signal);
    }

    private static SessionTransition RefreshOvertime(
        FocusSession session,
        TimeProvider timeProvider,
        long started,
        long timestamp)
    {
        TimeSpan active = Add(
            session.CommittedActiveDuration,
            GetElapsedTime(timeProvider, started, timestamp));
        return Transition(
            With(
                session,
                new OvertimeSessionState(timestamp),
                committedActiveDuration: active));
    }

    private static SessionTransition RefreshLanding(
        FocusSession session,
        TimeProvider timeProvider,
        long started,
        long timestamp)
    {
        TimeSpan elapsed = GetElapsedTime(timeProvider, started, timestamp);
        TimeSpan remaining = LandingLimit - session.LandingDuration;
        TimeSpan credited = elapsed > remaining ? remaining : elapsed;
        TimeSpan landing = Add(session.LandingDuration, credited);
        TimeSpan active = Add(session.CommittedActiveDuration, credited);
        bool reached = landing == LandingLimit;
        return Transition(
            With(
                session,
                reached
                    ? new LimitReachedSessionState(SessionBoundary.LandingLimit)
                    : new LandingSessionState(timestamp),
                committedActiveDuration: active,
                landingDuration: landing),
            reached ? SessionSignal.LandingLimitReached : SessionSignal.None);
    }

    private static SessionTransition RefreshBreak(
        FocusSession session,
        TimeProvider timeProvider,
        BreakSessionState @break,
        long timestamp)
    {
        TimeSpan elapsed = GetElapsedTime(
            timeProvider,
            @break.SegmentStartedTimestamp,
            timestamp);
        return Transition(
            With(
                session,
                new BreakSessionState(
                    timestamp,
                    @break.PriorOutcome,
                    @break.OutcomeAtUtc,
                    @break.ParkedNextPhysicalAction),
                breakDuration: Add(session.BreakDuration, elapsed)));
    }

    private static SessionCheckpoint CreateRecoveryCheckpoint(
        FocusSession session,
        ActiveSessionPhase phase,
        DateTimeOffset checkpointedAtUtc,
        SessionOutcome? priorOutcome = null,
        DateTimeOffset? outcomeAtUtc = null,
        string? parkedNextPhysicalAction = null)
    {
        return CreateCheckpoint(
            session,
            SessionCheckpointState.RecoveryRequired,
            checkpointedAtUtc,
            recoveryPhase: phase,
            priorOutcome: priorOutcome,
            completedAtUtc: priorOutcome == SessionOutcome.Completed ? outcomeAtUtc : null,
            parkedAtUtc: priorOutcome == SessionOutcome.Parked ? outcomeAtUtc : null,
            parkedNextPhysicalAction: parkedNextPhysicalAction);
    }

    private static SessionCheckpoint CreateCheckpoint(
        FocusSession session,
        SessionCheckpointState state,
        DateTimeOffset checkpointedAtUtc,
        ActiveSessionPhase? resumePhase = null,
        SessionBoundary? boundary = null,
        ActiveSessionPhase? recoveryPhase = null,
        SessionOutcome? priorOutcome = null,
        DateTimeOffset? completedAtUtc = null,
        DateTimeOffset? parkedAtUtc = null,
        DateTimeOffset? dayClosedAtUtc = null,
        string? parkedNextPhysicalAction = null)
    {
        return new SessionCheckpoint(
            session.Id,
            session.TaskId,
            session.TimingMode,
            session.OriginalPlannedDuration,
            session.ApprovedLimit,
            state,
            session.CommittedActiveDuration,
            session.LandingDuration,
            session.BreakDuration,
            checkpointedAtUtc,
            resumePhase,
            boundary,
            recoveryPhase,
            priorOutcome,
            session.StartedAtUtc,
            completedAtUtc,
            parkedAtUtc,
            dayClosedAtUtc,
            parkedNextPhysicalAction);
    }

    private static TimerReading CreateFocusReading(FocusSession session)
    {
        if (session.CommittedActiveDuration > session.ApprovedLimit)
        {
            return new OvertimeTimerReading(
                session.CommittedActiveDuration - session.ApprovedLimit);
        }

        return session.TimingMode switch
        {
            TimingMode.CountUp => new CountUpTimerReading(
                session.CommittedActiveDuration,
                session.ApprovedLimit),
            TimingMode.Countdown => new CountdownTimerReading(
                session.ApprovedLimit - session.CommittedActiveDuration,
                session.ApprovedLimit),
            _ => throw new InvalidOperationException("Session timing mode is invalid."),
        };
    }

    private static DateTimeOffset? GetOutcomeTimestamp(SessionCheckpoint checkpoint)
    {
        return checkpoint.PriorOutcome switch
        {
            SessionOutcome.Completed => checkpoint.CompletedAtUtc,
            SessionOutcome.Parked => checkpoint.ParkedAtUtc,
            _ => null,
        };
    }

    private static TState RequireState<TState>(FocusSession session, string commandName)
        where TState : SessionState
    {
        return session.State as TState
            ?? throw new InvalidOperationException(
                $"Command '{commandName}' is not legal from state '{session.State.Kind}'.");
    }

    private static SessionTransition Invalid(FocusSession session, string commandName)
    {
        throw new InvalidOperationException(
            $"Command '{commandName}' is not legal from state '{session.State.Kind}'.");
    }

    private static SessionTransition Transition(
        FocusSession session,
        SessionSignal signal = SessionSignal.None)
    {
        return new SessionTransition(session, signal);
    }

    private static FocusSession With(
        FocusSession session,
        SessionState state,
        TimeSpan? approvedLimit = null,
        TimeSpan? committedActiveDuration = null,
        TimeSpan? landingDuration = null,
        TimeSpan? breakDuration = null,
        DateTimeOffset? startedAtUtc = null)
    {
        return new FocusSession(
            session.Id,
            session.TaskId,
            session.TimingMode,
            session.OriginalPlannedDuration,
            approvedLimit ?? session.ApprovedLimit,
            committedActiveDuration ?? session.CommittedActiveDuration,
            landingDuration ?? session.LandingDuration,
            breakDuration ?? session.BreakDuration,
            startedAtUtc ?? session.StartedAtUtc,
            state);
    }

    private static TimeSpan GetElapsedTime(
        TimeProvider timeProvider,
        long started,
        long timestamp)
    {
        TimeSpan elapsed = timeProvider.GetElapsedTime(started, timestamp);
        if (elapsed < TimeSpan.Zero)
        {
            throw new InvalidOperationException("The monotonic clock moved backwards.");
        }

        return elapsed;
    }

    private static TimeSpan Add(TimeSpan left, TimeSpan right)
    {
        return TimeSpan.FromTicks(checked(left.Ticks + right.Ticks));
    }
}
