using NowNext.Core.Sessions;

namespace NowNext.Core.Tests.Sessions;

[TestClass]
public sealed class SessionTransitionMatrixTests
{
    [TestMethod]
    public void ApplyEveryStateAndCommandPairMatchesExplicitLegalTransitionMatrix()
    {
        foreach (StateCase stateCase in CreateStateCases())
        {
            foreach (CommandCase commandCase in CreateCommandCases())
            {
                SessionCommand command = commandCase.Create();
                if (stateCase.LegalCommands.Contains(command.GetType()))
                {
                    FocusSessionMachineTests.Apply(
                        stateCase.Session,
                        command,
                        stateCase.Clock);
                }
                else
                {
                    InvalidOperationException exception =
                        Assert.ThrowsExactly<InvalidOperationException>(
                            () => FocusSessionMachineTests.Apply(
                                stateCase.Session,
                                command,
                                stateCase.Clock),
                            $"{command.GetType().Name} should be illegal from {stateCase.Name}.");
                    Assert.Contains(stateCase.Session.State.Kind.ToString(), exception.Message);
                }
            }
        }
    }

    [TestMethod]
    public void FixedSeedLegalSequencesPreserveSessionInvariants()
    {
        const int seed = 0x4E4F57;
        var random = new Random(seed);

        for (int sequence = 0; sequence < 50; sequence++)
        {
            var clock = new SessionTestClock(timestamp: sequence * 10_000L);
            FocusSession session = FocusSessionMachineTests.CreateSession(
                sequence % 2 == 0
                    ? NowNext.Core.Domain.TimingMode.CountUp
                    : NowNext.Core.Domain.TimingMode.Countdown);
            SessionId originalId = session.Id;
            NowNext.Core.Domain.TaskId originalTaskId = session.TaskId;
            NowNext.Core.Domain.TimingMode originalMode = session.TimingMode;
            TimeSpan previousActive = session.CommittedActiveDuration;
            TimeSpan previousBreak = session.BreakDuration;

            for (int step = 0; step < 100; step++)
            {
                clock.Advance(TimeSpan.FromSeconds(random.Next(0, 91)));
                SessionCommand[] legal = LegalCommandsFor(session.State);
                SessionCommand command = legal[random.Next(legal.Length)];
                session = FocusSessionMachineTests.Apply(session, command, clock).Session;

                Assert.AreEqual(originalId, session.Id);
                Assert.AreEqual(originalTaskId, session.TaskId);
                Assert.AreEqual(originalMode, session.TimingMode);
                Assert.IsGreaterThanOrEqualTo(previousActive, session.CommittedActiveDuration);
                Assert.IsGreaterThanOrEqualTo(previousBreak, session.BreakDuration);
                Assert.IsGreaterThanOrEqualTo(TimeSpan.Zero, session.CommittedActiveDuration);
                Assert.IsGreaterThanOrEqualTo(TimeSpan.Zero, session.BreakDuration);
                Assert.IsGreaterThanOrEqualTo(TimeSpan.Zero, session.LandingDuration);
                Assert.IsLessThanOrEqualTo(
                    FocusSessionMachine.LandingLimit,
                    session.LandingDuration);
                Assert.IsLessThanOrEqualTo(
                    session.CommittedActiveDuration,
                    session.LandingDuration);

                previousActive = session.CommittedActiveDuration;
                previousBreak = session.BreakDuration;
            }
        }
    }

    private static IReadOnlyList<StateCase> CreateStateCases()
    {
        return
        [
            CreateCase(
                "Ready",
                static (session, _) => session,
                typeof(RefreshSession), typeof(StartSession), typeof(CloseDay)),
            CreateCase(
                "Focusing",
                static (session, clock) => Apply(session, clock, new StartSession()),
                typeof(RefreshSession), typeof(PauseSession), typeof(CompleteSession),
                typeof(ParkSession), typeof(AbandonSession), typeof(InterruptSession)),
            CreateCase(
                "Paused",
                static (session, clock) => Apply(
                    Apply(session, clock, new StartSession()),
                    clock,
                    new PauseSession()),
                typeof(RefreshSession), typeof(ResumeSession), typeof(CompleteSession),
                typeof(ParkSession), typeof(AbandonSession)),
            CreateCase(
                "FocusLimitReached",
                static (session, clock) => ReachFocusLimit(session, clock),
                typeof(RefreshSession), typeof(ContinueOvertime), typeof(BeginLanding),
                typeof(ExtendSession), typeof(CompleteSession), typeof(ParkSession),
                typeof(AbandonSession)),
            CreateCase(
                "Overtime",
                static (session, clock) => ReachOvertime(session, clock),
                typeof(RefreshSession), typeof(PauseSession), typeof(BeginLanding),
                typeof(ExtendSession), typeof(CompleteSession), typeof(ParkSession),
                typeof(AbandonSession), typeof(InterruptSession)),
            CreateCase(
                "Landing",
                static (session, clock) => Apply(
                    ReachFocusLimit(session, clock),
                    clock,
                    new BeginLanding()),
                typeof(RefreshSession), typeof(ExtendSession), typeof(CompleteSession),
                typeof(ParkSession), typeof(AbandonSession), typeof(InterruptSession)),
            CreateCase(
                "LandingLimitReached",
                static (session, clock) => ReachLandingLimit(session, clock),
                typeof(RefreshSession), typeof(ExtendSession), typeof(CompleteSession),
                typeof(ParkSession), typeof(AbandonSession)),
            CreateCase(
                "Completed",
                static (session, clock) => Apply(
                    Apply(session, clock, new StartSession()),
                    clock,
                    new CompleteSession()),
                typeof(RefreshSession), typeof(BeginBreak), typeof(CloseDay)),
            CreateCase(
                "Parked",
                static (session, clock) => Apply(
                    Apply(session, clock, new StartSession()),
                    clock,
                    new ParkSession("Next action")),
                typeof(RefreshSession), typeof(BeginBreak), typeof(CloseDay)),
            CreateCase(
                "Break",
                static (session, clock) => Apply(
                    Apply(
                        Apply(session, clock, new StartSession()),
                        clock,
                        new CompleteSession()),
                    clock,
                    new BeginBreak()),
                typeof(RefreshSession), typeof(EndBreak), typeof(InterruptSession),
                typeof(CloseDay)),
            CreateCase(
                "BreakCompleted",
                static (session, clock) => ReachBreakLimit(session, clock),
                typeof(RefreshSession), typeof(EndBreak), typeof(CloseDay)),
            CreateCase(
                "RecoveryRequired",
                static (session, clock) => Apply(
                    Apply(session, clock, new StartSession()),
                    clock,
                    new InterruptSession()),
                typeof(RefreshSession), typeof(CompleteSession), typeof(ParkSession),
                typeof(AbandonSession), typeof(ResumeWithoutAwayTime),
                typeof(ResumeIncludingAwayTime)),
            CreateCase(
                "BreakRecoveryRequired",
                static (session, clock) => Apply(
                    Apply(
                        Apply(
                            Apply(session, clock, new StartSession()),
                            clock,
                            new CompleteSession()),
                        clock,
                        new BeginBreak()),
                    clock,
                    new InterruptSession()),
                typeof(RefreshSession), typeof(ResumeWithoutAwayTime),
                typeof(ResumeIncludingAwayTime)),
            CreateCase(
                "Abandoned",
                static (session, clock) => Apply(
                    Apply(session, clock, new StartSession()),
                    clock,
                    new AbandonSession()),
                typeof(RefreshSession)),
            CreateCase(
                "DayClosed",
                static (session, clock) => Apply(session, clock, new CloseDay()),
                typeof(RefreshSession)),
        ];
    }

    private static IReadOnlyList<CommandCase> CreateCommandCases()
    {
        return
        [
            new(typeof(StartSession), static () => new StartSession()),
            new(typeof(RefreshSession), static () => new RefreshSession()),
            new(typeof(PauseSession), static () => new PauseSession()),
            new(typeof(ResumeSession), static () => new ResumeSession()),
            new(typeof(ContinueOvertime), static () => new ContinueOvertime()),
            new(typeof(BeginLanding), static () => new BeginLanding()),
            new(typeof(ExtendSession), static () => new ExtendSession(TimeSpan.FromMinutes(1))),
            new(typeof(CompleteSession), static () => new CompleteSession()),
            new(typeof(ParkSession), static () => new ParkSession("Next action")),
            new(typeof(AbandonSession), static () => new AbandonSession()),
            new(typeof(BeginBreak), static () => new BeginBreak()),
            new(typeof(EndBreak), static () => new EndBreak()),
            new(typeof(InterruptSession), static () => new InterruptSession()),
            new(typeof(ResumeWithoutAwayTime), static () => new ResumeWithoutAwayTime()),
            new(typeof(ResumeIncludingAwayTime),
                static () => new ResumeIncludingAwayTime(TimeSpan.Zero)),
            new(typeof(CloseDay), static () => new CloseDay()),
        ];
    }

    private static SessionCommand[] LegalCommandsFor(SessionState state)
    {
        return state switch
        {
            ReadySessionState => [new StartSession(), new RefreshSession(), new CloseDay()],
            FocusingSessionState =>
                [new RefreshSession(), new PauseSession(), new CompleteSession(),
                 new ParkSession("Next action"), new AbandonSession(),
                 new InterruptSession()],
            PausedSessionState =>
                [new RefreshSession(), new ResumeSession(), new CompleteSession(),
                 new ParkSession("Next action"), new AbandonSession()],
            LimitReachedSessionState { Boundary: SessionBoundary.FocusLimit } =>
                [new RefreshSession(), new ContinueOvertime(), new BeginLanding(),
                 new ExtendSession(TimeSpan.FromMinutes(1)), new CompleteSession(),
                 new ParkSession("Next action"), new AbandonSession()],
            LimitReachedSessionState =>
                [new RefreshSession(), new ExtendSession(TimeSpan.FromMinutes(1)),
                 new CompleteSession(), new ParkSession("Next action"),
                 new AbandonSession()],
            OvertimeSessionState =>
                [new RefreshSession(), new PauseSession(), new BeginLanding(),
                 new ExtendSession(TimeSpan.FromMinutes(1)), new CompleteSession(),
                 new ParkSession("Next action"), new AbandonSession(),
                 new InterruptSession()],
            LandingSessionState =>
                [new RefreshSession(), new ExtendSession(TimeSpan.FromMinutes(1)),
                 new CompleteSession(), new ParkSession("Next action"),
                 new AbandonSession(), new InterruptSession()],
            CompletedSessionState or ParkedSessionState =>
                [new RefreshSession(), new BeginBreak(), new CloseDay()],
            BreakSessionState =>
                [new RefreshSession(), new EndBreak(), new InterruptSession(), new CloseDay()],
            BreakCompletedSessionState => [new RefreshSession(), new EndBreak(), new CloseDay()],
            RecoveryRequiredSessionState { InterruptedPhase: ActiveSessionPhase.Break } =>
                [new RefreshSession(), new ResumeWithoutAwayTime(),
                 new ResumeIncludingAwayTime(TimeSpan.Zero)],
            RecoveryRequiredSessionState =>
                [new RefreshSession(), new ResumeWithoutAwayTime(),
                 new ResumeIncludingAwayTime(TimeSpan.Zero), new CompleteSession(),
                 new ParkSession("Next action"), new AbandonSession()],
            AbandonedSessionState => [new RefreshSession()],
            DayClosedSessionState => [new RefreshSession()],
            _ => throw new InvalidOperationException("The state is not represented by the test model."),
        };
    }

    private static StateCase CreateCase(
        string name,
        Func<FocusSession, SessionTestClock, FocusSession> arrange,
        params Type[] legalCommands)
    {
        var clock = new SessionTestClock();
        FocusSession session = arrange(FocusSessionMachineTests.CreateSession(), clock);
        return new StateCase(name, session, clock, legalCommands.ToHashSet());
    }

    private static FocusSession ReachFocusLimit(FocusSession session, SessionTestClock clock)
    {
        session = Apply(session, clock, new StartSession());
        clock.Advance(TimeSpan.FromMinutes(25));
        return Apply(session, clock, new RefreshSession());
    }

    private static FocusSession ReachOvertime(FocusSession session, SessionTestClock clock)
    {
        session = Apply(session, clock, new StartSession());
        clock.Advance(TimeSpan.FromMinutes(26));
        return Apply(session, clock, new RefreshSession());
    }

    private static FocusSession ReachLandingLimit(FocusSession session, SessionTestClock clock)
    {
        session = Apply(ReachFocusLimit(session, clock), clock, new BeginLanding());
        clock.Advance(TimeSpan.FromMinutes(5));
        return Apply(session, clock, new RefreshSession());
    }

    private static FocusSession ReachBreakLimit(FocusSession session, SessionTestClock clock)
    {
        session = Apply(session, clock, new StartSession());
        session = Apply(session, clock, new CompleteSession());
        session = Apply(session, clock, new BeginBreak());
        clock.Advance(TimeSpan.FromMinutes(5));
        return Apply(session, clock, new RefreshSession());
    }

    private static FocusSession Apply(
        FocusSession session,
        SessionTestClock clock,
        SessionCommand command)
    {
        return FocusSessionMachineTests.Apply(session, command, clock).Session;
    }

    private sealed record StateCase(
        string Name,
        FocusSession Session,
        SessionTestClock Clock,
        IReadOnlySet<Type> LegalCommands);

    private sealed record CommandCase(Type Type, Func<SessionCommand> Create);
}
