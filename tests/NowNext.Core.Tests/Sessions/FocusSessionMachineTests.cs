using NowNext.Core.Domain;
using NowNext.Core.Sessions;

namespace NowNext.Core.Tests.Sessions;

[TestClass]
public sealed class FocusSessionMachineTests
{
    private static readonly TimeSpan PlannedDuration = TimeSpan.FromMinutes(25);

    [TestMethod]
    [DataRow(TimingMode.CountUp)]
    [DataRow(TimingMode.Countdown)]
    public void StartAndRefreshBothTimingModesUseMonotonicElapsedTime(TimingMode timingMode)
    {
        var clock = new SessionTestClock();
        FocusSession session = CreateSession(timingMode);

        session = Apply(session, new StartSession(), clock).Session;
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(4));
        SessionView view = FocusSessionMachine.CreateView(session, clock);

        Assert.AreEqual(SessionStateKind.Focusing, view.State);
        if (timingMode == TimingMode.CountUp)
        {
            CountUpTimerReading reading = Assert.IsInstanceOfType<CountUpTimerReading>(view.Timer);
            Assert.AreEqual(TimeSpan.FromMinutes(4), reading.Elapsed);
            Assert.AreEqual(PlannedDuration, reading.Limit);
        }
        else
        {
            CountdownTimerReading reading =
                Assert.IsInstanceOfType<CountdownTimerReading>(view.Timer);
            Assert.AreEqual(TimeSpan.FromMinutes(21), reading.Remaining);
            Assert.AreEqual(PlannedDuration, reading.Limit);
        }
    }

    [TestMethod]
    public void PauseAndResumeExcludesPausedTimeAndPreservesActiveSegments()
    {
        var clock = new SessionTestClock();
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;
        clock.Advance(TimeSpan.FromMinutes(3));

        session = Apply(session, new PauseSession(), clock).Session;
        Assert.AreEqual(SessionStateKind.Paused, session.State.Kind);
        Assert.AreEqual(TimeSpan.FromMinutes(3), session.CommittedActiveDuration);
        clock.Advance(TimeSpan.FromHours(2));
        SessionView pausedView = FocusSessionMachine.CreateView(session, clock);
        CountUpTimerReading pausedReading =
            Assert.IsInstanceOfType<CountUpTimerReading>(pausedView.Timer);
        Assert.AreEqual(TimeSpan.FromMinutes(3), pausedReading.Elapsed);

        session = Apply(session, new ResumeSession(), clock).Session;
        clock.Advance(TimeSpan.FromMinutes(2));
        session = Apply(session, new RefreshSession(), clock).Session;

        Assert.AreEqual(SessionStateKind.Focusing, session.State.Kind);
        Assert.AreEqual(TimeSpan.FromMinutes(5), session.CommittedActiveDuration);
    }

    [TestMethod]
    public void DelayedAndPartitionedRefreshProduceIdenticalElapsedTime()
    {
        var delayedClock = new SessionTestClock();
        var partitionedClock = new SessionTestClock();
        FocusSession delayed = Apply(CreateSession(), new StartSession(), delayedClock).Session;
        FocusSession partitioned = Apply(CreateSession(), new StartSession(), partitionedClock).Session;

        delayedClock.AdvanceMonotonic(TimeSpan.FromMinutes(17));
        delayed = Apply(delayed, new RefreshSession(), delayedClock).Session;
        foreach (TimeSpan interval in new[]
                 {
                     TimeSpan.FromSeconds(1),
                     TimeSpan.FromMinutes(2),
                     TimeSpan.FromMinutes(7),
                     TimeSpan.FromMinutes(8) - TimeSpan.FromSeconds(1),
                 })
        {
            partitionedClock.AdvanceMonotonic(interval);
            partitioned = Apply(partitioned, new RefreshSession(), partitionedClock).Session;
        }

        Assert.AreEqual(delayed.CommittedActiveDuration, partitioned.CommittedActiveDuration);
        Assert.AreEqual(TimeSpan.FromMinutes(17), delayed.CommittedActiveDuration);
        Assert.AreEqual(delayed.State.Kind, partitioned.State.Kind);
    }

    [TestMethod]
    public void RefreshAtExactFocusBoundarySignalsOnceAndIsIdempotent()
    {
        var clock = new SessionTestClock();
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;
        clock.AdvanceMonotonic(PlannedDuration);

        SessionTransition boundary = Apply(session, new RefreshSession(), clock);
        SessionTransition repeated = Apply(boundary.Session, new RefreshSession(), clock);

        Assert.AreEqual(SessionSignal.FocusLimitReached, boundary.Signal);
        LimitReachedSessionState state =
            Assert.IsInstanceOfType<LimitReachedSessionState>(boundary.Session.State);
        Assert.AreEqual(SessionBoundary.FocusLimit, state.Boundary);
        Assert.AreEqual(PlannedDuration, boundary.Session.CommittedActiveDuration);
        Assert.AreEqual(SessionSignal.None, repeated.Signal);
        Assert.AreEqual(boundary.Session, repeated.Session);
    }

    [TestMethod]
    public void RefreshFirstObservationBeyondLimitPreservesOvertimeAndSignalsOnce()
    {
        var clock = new SessionTestClock();
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;
        clock.AdvanceMonotonic(PlannedDuration + TimeSpan.FromMinutes(3));

        SessionTransition transition = Apply(session, new RefreshSession(), clock);
        SessionTransition repeated = Apply(transition.Session, new RefreshSession(), clock);
        SessionView view = FocusSessionMachine.CreateView(repeated.Session, clock);

        Assert.AreEqual(SessionStateKind.Overtime, transition.Session.State.Kind);
        Assert.AreEqual(SessionSignal.FocusLimitReached, transition.Signal);
        Assert.AreEqual(SessionSignal.None, repeated.Signal);
        Assert.AreEqual(PlannedDuration + TimeSpan.FromMinutes(3),
            repeated.Session.CommittedActiveDuration);
        OvertimeTimerReading reading = Assert.IsInstanceOfType<OvertimeTimerReading>(view.Timer);
        Assert.AreEqual(TimeSpan.FromMinutes(3), reading.Overtime);
    }

    [TestMethod]
    public void ContinueOvertimeDoesNotCountTimeSpentAtBoundary()
    {
        var clock = new SessionTestClock();
        FocusSession session = ReachFocusLimit(clock);
        clock.Advance(TimeSpan.FromHours(1));

        session = Apply(session, new ContinueOvertime(), clock).Session;
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(2));
        session = Apply(session, new RefreshSession(), clock).Session;

        Assert.AreEqual(SessionStateKind.Overtime, session.State.Kind);
        Assert.AreEqual(PlannedDuration + TimeSpan.FromMinutes(2),
            session.CommittedActiveDuration);
    }

    [TestMethod]
    public void ExtendFromOvertimeGrantsFullExtensionFromCommandInstant()
    {
        var clock = new SessionTestClock();
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;
        clock.Advance(TimeSpan.FromMinutes(28));
        session = Apply(session, new RefreshSession(), clock).Session;

        SessionTransition extended = Apply(
            session,
            new ExtendSession(TimeSpan.FromMinutes(10)),
            clock);
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(9));
        FocusSession beforeNewLimit = Apply(
            extended.Session,
            new RefreshSession(),
            clock).Session;

        Assert.AreEqual(TimeSpan.FromMinutes(38), extended.Session.ApprovedLimit);
        Assert.AreEqual(SessionStateKind.Focusing, beforeNewLimit.State.Kind);
        Assert.AreEqual(TimeSpan.FromMinutes(37), beforeNewLimit.CommittedActiveDuration);
        Assert.AreEqual(PlannedDuration, beforeNewLimit.OriginalPlannedDuration);
    }

    [TestMethod]
    public void LandingAtAndBeyondFiveMinutesClampsAndSignalsOnce()
    {
        var clock = new SessionTestClock();
        FocusSession session = ReachFocusLimit(clock);
        session = Apply(session, new BeginLanding(), clock).Session;
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(8));

        SessionTransition boundary = Apply(session, new RefreshSession(), clock);
        SessionTransition repeated = Apply(boundary.Session, new RefreshSession(), clock);
        SessionView view = FocusSessionMachine.CreateView(repeated.Session, clock);

        Assert.AreEqual(SessionSignal.LandingLimitReached, boundary.Signal);
        Assert.AreEqual(SessionSignal.None, repeated.Signal);
        Assert.AreEqual(FocusSessionMachine.LandingLimit, boundary.Session.LandingDuration);
        Assert.AreEqual(TimeSpan.FromMinutes(30), boundary.Session.CommittedActiveDuration);
        LimitReachedSessionState state =
            Assert.IsInstanceOfType<LimitReachedSessionState>(boundary.Session.State);
        Assert.AreEqual(SessionBoundary.LandingLimit, state.Boundary);
        LandingTimerReading reading = Assert.IsInstanceOfType<LandingTimerReading>(view.Timer);
        Assert.AreEqual(FocusSessionMachine.LandingLimit, reading.Elapsed);
    }

    [TestMethod]
    public void CompleteBeginAndEndBreakTracksBreakSeparatelyAndReturnsOutcome()
    {
        var clock = new SessionTestClock();
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;
        clock.Advance(TimeSpan.FromMinutes(7));

        session = Apply(session, new CompleteSession(), clock).Session;
        CompletedSessionState completed =
            Assert.IsInstanceOfType<CompletedSessionState>(session.State);
        session = Apply(session, new BeginBreak(), clock).Session;
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(6));
        SessionView breakView = FocusSessionMachine.CreateView(session, clock);
        BreakTimerReading breakReading = Assert.IsInstanceOfType<BreakTimerReading>(breakView.Timer);
        session = Apply(session, new EndBreak(), clock).Session;

        Assert.AreEqual(TimeSpan.FromMinutes(7), session.CommittedActiveDuration);
        Assert.AreEqual(TimeSpan.FromMinutes(6), session.BreakDuration);
        CompletedSessionState outcome =
            Assert.IsInstanceOfType<CompletedSessionState>(session.State);
        Assert.AreEqual(completed.CompletedAtUtc, outcome.CompletedAtUtc);
        Assert.AreEqual(TimeSpan.FromMinutes(6), breakReading.Elapsed);
    }

    [TestMethod]
    public void ParkBreakAndCloseDayPreservesTrimmedParkedOutcome()
    {
        var clock = new SessionTestClock();
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;
        clock.Advance(TimeSpan.FromMinutes(1));

        session = Apply(session, new ParkSession("  Open the next section  "), clock).Session;
        ParkedSessionState parked = Assert.IsInstanceOfType<ParkedSessionState>(session.State);
        Assert.AreEqual("Open the next section", parked.NextPhysicalAction);
        session = Apply(session, new BeginBreak(), clock).Session;
        clock.Advance(TimeSpan.FromMinutes(4));
        session = Apply(session, new CloseDay(), clock).Session;

        DayClosedSessionState closed =
            Assert.IsInstanceOfType<DayClosedSessionState>(session.State);
        Assert.AreEqual(SessionOutcome.Parked, closed.PriorOutcome);
        Assert.AreEqual(parked.ParkedAtUtc, closed.OutcomeAtUtc);
        Assert.AreEqual("Open the next section", closed.ParkedNextPhysicalAction);
        Assert.AreEqual(TimeSpan.FromMinutes(4), session.BreakDuration);
    }

    [TestMethod]
    public void CloseDayFromReadyAndCompletedIsTerminal()
    {
        var clock = new SessionTestClock();
        FocusSession readyClosed = Apply(CreateSession(), new CloseDay(), clock).Session;
        FocusSession completed = Apply(CreateSession(), new StartSession(), clock).Session;
        completed = Apply(completed, new CompleteSession(), clock).Session;
        FocusSession completedClosed = Apply(completed, new CloseDay(), clock).Session;

        DayClosedSessionState readyState =
            Assert.IsInstanceOfType<DayClosedSessionState>(readyClosed.State);
        DayClosedSessionState completedState =
            Assert.IsInstanceOfType<DayClosedSessionState>(completedClosed.State);
        Assert.IsNull(readyState.PriorOutcome);
        Assert.AreEqual(SessionOutcome.Completed, completedState.PriorOutcome);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => Apply(readyClosed, new StartSession(), clock));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => Apply(completedClosed, new BeginBreak(), clock));
    }

    [TestMethod]
    public void InvalidCommandsAreRejectedWithStateAndCommandInMessage()
    {
        var clock = new SessionTestClock();
        FocusSession ready = CreateSession();
        (FocusSession Session, SessionCommand Command)[] cases =
        [
            (ready, new PauseSession()),
            (ready, new ResumeSession()),
            (ready, new BeginLanding()),
            (ready, new CompleteSession()),
            (Apply(ready, new StartSession(), clock).Session, new BeginBreak()),
            (ReachFocusLimit(new SessionTestClock()), new ResumeSession()),
        ];

        foreach ((FocusSession session, SessionCommand command) in cases)
        {
            InvalidOperationException exception =
                Assert.ThrowsExactly<InvalidOperationException>(
                    () => Apply(session, command, clock));
            Assert.Contains(command.GetType().Name, exception.Message);
            Assert.Contains(session.State.Kind.ToString(), exception.Message);
        }
    }

    [TestMethod]
    public void CommandValueObjectsRejectInvalidDurationsAndParkingText()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ExtendSession(TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentException>(
            () => new ExtendSession(TimeSpan.FromTicks(-1)));
        Assert.ThrowsExactly<ArgumentException>(
            () => new ResumeIncludingAwayTime(TimeSpan.FromTicks(-1)));
        Assert.ThrowsExactly<ArgumentException>(() => new ParkSession("  "));
    }

    [TestMethod]
    public void CustomTimestampFrequencyIsConvertedThroughTimeProvider()
    {
        var clock = new SessionTestClock(timestamp: 123, timestampFrequency: 1_000);
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;

        clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(1_250));
        session = Apply(session, new RefreshSession(), clock).Session;

        Assert.AreEqual(TimeSpan.FromMilliseconds(1_250), session.CommittedActiveDuration);
    }

    [TestMethod]
    public void WallClockChangesDoNotAlterActiveElapsedTime()
    {
        var clock = new SessionTestClock();
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(3));
        clock.SetUtcNow(clock.GetUtcNow().AddDays(-10));
        session = Apply(session, new RefreshSession(), clock).Session;
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(2));
        clock.SetUtcNow(clock.GetUtcNow().AddDays(20));
        session = Apply(session, new RefreshSession(), clock).Session;

        Assert.AreEqual(TimeSpan.FromMinutes(5), session.CommittedActiveDuration);
        Assert.AreEqual(SessionStateKind.Focusing, session.State.Kind);
    }

    private static FocusSession ReachFocusLimit(SessionTestClock clock)
    {
        FocusSession session = Apply(CreateSession(), new StartSession(), clock).Session;
        clock.AdvanceMonotonic(PlannedDuration);
        return Apply(session, new RefreshSession(), clock).Session;
    }

    internal static FocusSession CreateSession(TimingMode timingMode = TimingMode.CountUp)
    {
        return FocusSessionMachine.Create(
            new SessionId(Guid.Parse("237ca822-ef2e-45b7-af7f-8466b8ebd87b")),
            new TaskId(Guid.Parse("d403426b-7bf4-48e8-8b28-5ea4c02673a9")),
            timingMode,
            PlannedDuration);
    }

    internal static SessionTransition Apply(
        FocusSession session,
        SessionCommand command,
        TimeProvider clock)
    {
        return FocusSessionMachine.Apply(session, command, clock);
    }
}
