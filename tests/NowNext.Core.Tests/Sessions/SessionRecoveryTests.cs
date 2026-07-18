using NowNext.Core.Domain;
using NowNext.Core.Sessions;

namespace NowNext.Core.Tests.Sessions;

[TestClass]
public sealed class SessionRecoveryTests
{
    [TestMethod]
    [DataRow(TimingMode.CountUp)]
    [DataRow(TimingMode.Countdown)]
    public void CheckpointAndRestoreRunningFocusRestoresOnlyCommittedTime(
        TimingMode timingMode)
    {
        var firstClock = new SessionTestClock(timestamp: 51_000);
        FocusSession running = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(timingMode),
            new StartSession(),
            firstClock).Session;
        firstClock.Advance(TimeSpan.FromMinutes(4));
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(running, firstClock);

        var restartedClock = new SessionTestClock(
            firstClock.GetUtcNow().AddHours(6),
            timestamp: -9_000_000);
        FocusSession restored = FocusSessionMachine.Restore(checkpoint, restartedClock);

        Assert.AreEqual(SessionCheckpointState.RecoveryRequired, checkpoint.State);
        Assert.AreEqual(ActiveSessionPhase.Focusing, checkpoint.RecoveryPhase);
        Assert.AreEqual(TimeSpan.FromMinutes(4), checkpoint.CommittedActiveDuration);
        Assert.AreEqual(SessionStateKind.RecoveryRequired, restored.State.Kind);
        Assert.AreEqual(TimeSpan.FromMinutes(4), restored.CommittedActiveDuration);
        Assert.AreEqual(checkpoint.Id, restored.Id);
        Assert.AreEqual(checkpoint.TaskId, restored.TaskId);
        Assert.AreEqual(timingMode, restored.TimingMode);
    }

    [TestMethod]
    public void ResumeWithoutAwayTimeStartsFreshSegmentWithoutInventingTime()
    {
        var firstClock = new SessionTestClock();
        FocusSession session = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            firstClock).Session;
        firstClock.Advance(TimeSpan.FromMinutes(3));
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(session, firstClock);
        var restartedClock = new SessionTestClock(
            firstClock.GetUtcNow().AddHours(8),
            timestamp: 700);
        session = FocusSessionMachine.Restore(checkpoint, restartedClock);

        session = FocusSessionMachineTests.Apply(
            session,
            new ResumeWithoutAwayTime(),
            restartedClock).Session;
        Assert.AreEqual(TimeSpan.FromMinutes(3), session.CommittedActiveDuration);
        restartedClock.AdvanceMonotonic(TimeSpan.FromMinutes(2));
        session = FocusSessionMachineTests.Apply(
            session,
            new RefreshSession(),
            restartedClock).Session;

        Assert.AreEqual(TimeSpan.FromMinutes(5), session.CommittedActiveDuration);
    }

    [TestMethod]
    public void ResumeIncludingAwayTimeWithinObservedGapCreditsExplicitDurationOnly()
    {
        var firstClock = new SessionTestClock();
        FocusSession running = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            firstClock).Session;
        firstClock.Advance(TimeSpan.FromMinutes(4));
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(running, firstClock);
        var restartedClock = new SessionTestClock(firstClock.GetUtcNow().AddMinutes(10));
        FocusSession restored = FocusSessionMachine.Restore(checkpoint, restartedClock);

        FocusSession resumed = FocusSessionMachineTests.Apply(
            restored,
            new ResumeIncludingAwayTime(TimeSpan.FromMinutes(6)),
            restartedClock).Session;

        Assert.AreEqual(TimeSpan.FromMinutes(10), resumed.CommittedActiveDuration);
        Assert.AreEqual(SessionStateKind.Focusing, resumed.State.Kind);
    }

    [TestMethod]
    public void ResumeIncludingAwayTimeAboveObservedGapIsRejectedWithoutMutation()
    {
        var firstClock = new SessionTestClock();
        FocusSession running = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            firstClock).Session;
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(running, firstClock);
        var restartedClock = new SessionTestClock(firstClock.GetUtcNow().AddMinutes(5));
        FocusSession restored = FocusSessionMachine.Restore(checkpoint, restartedClock);

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => FocusSessionMachineTests.Apply(
                restored,
                new ResumeIncludingAwayTime(TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1)),
                restartedClock));

        Assert.Contains("exceeds", exception.Message);
        Assert.AreEqual(TimeSpan.Zero, restored.CommittedActiveDuration);
        Assert.AreEqual(SessionStateKind.RecoveryRequired, restored.State.Kind);
    }

    [TestMethod]
    public void ResumeIncludingAwayTimeAfterBackwardUtcChangeAllowsOnlyZero()
    {
        var firstClock = new SessionTestClock();
        FocusSession running = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            firstClock).Session;
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(running, firstClock);
        var restartedClock = new SessionTestClock(firstClock.GetUtcNow().AddHours(-1));
        FocusSession restored = FocusSessionMachine.Restore(checkpoint, restartedClock);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => FocusSessionMachineTests.Apply(
                restored,
                new ResumeIncludingAwayTime(TimeSpan.FromTicks(1)),
                restartedClock));
        FocusSession zeroIncluded = FocusSessionMachineTests.Apply(
            restored,
            new ResumeIncludingAwayTime(TimeSpan.Zero),
            restartedClock).Session;

        Assert.AreEqual(TimeSpan.Zero, zeroIncluded.CommittedActiveDuration);
        Assert.AreEqual(SessionStateKind.Focusing, zeroIncluded.State.Kind);
    }

    [TestMethod]
    public void ResumeIncludingAwayTimeCrossingFocusLimitEntersOvertimeAndSignalsOnce()
    {
        var firstClock = new SessionTestClock();
        FocusSession running = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            firstClock).Session;
        firstClock.Advance(TimeSpan.FromMinutes(24));
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(running, firstClock);
        var restartedClock = new SessionTestClock(firstClock.GetUtcNow().AddMinutes(5));
        FocusSession restored = FocusSessionMachine.Restore(checkpoint, restartedClock);

        SessionTransition resumed = FocusSessionMachineTests.Apply(
            restored,
            new ResumeIncludingAwayTime(TimeSpan.FromMinutes(2)),
            restartedClock);
        SessionTransition repeated = FocusSessionMachineTests.Apply(
            resumed.Session,
            new RefreshSession(),
            restartedClock);

        Assert.AreEqual(SessionStateKind.Overtime, resumed.Session.State.Kind);
        Assert.AreEqual(TimeSpan.FromMinutes(26), resumed.Session.CommittedActiveDuration);
        Assert.AreEqual(SessionSignal.FocusLimitReached, resumed.Signal);
        Assert.AreEqual(SessionSignal.None, repeated.Signal);
    }

    [TestMethod]
    public void LandingRecoveryClampsIncludedTimeToRemainingFiveMinutes()
    {
        var firstClock = new SessionTestClock();
        FocusSession session = ReachLanding(firstClock);
        firstClock.Advance(TimeSpan.FromMinutes(2));
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(session, firstClock);
        var restartedClock = new SessionTestClock(firstClock.GetUtcNow().AddMinutes(20));
        FocusSession restored = FocusSessionMachine.Restore(checkpoint, restartedClock);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => FocusSessionMachineTests.Apply(
                restored,
                new ResumeIncludingAwayTime(TimeSpan.FromMinutes(3) + TimeSpan.FromTicks(1)),
                restartedClock));
        SessionTransition resumed = FocusSessionMachineTests.Apply(
            restored,
            new ResumeIncludingAwayTime(TimeSpan.FromMinutes(3)),
            restartedClock);

        Assert.AreEqual(FocusSessionMachine.LandingLimit, resumed.Session.LandingDuration);
        Assert.AreEqual(TimeSpan.FromMinutes(30), resumed.Session.CommittedActiveDuration);
        Assert.AreEqual(SessionSignal.LandingLimitReached, resumed.Signal);
        LimitReachedSessionState state =
            Assert.IsInstanceOfType<LimitReachedSessionState>(resumed.Session.State);
        Assert.AreEqual(SessionBoundary.LandingLimit, state.Boundary);
    }

    [TestMethod]
    public void BreakRecoveryIncludedAwayTimeChangesBreakOnlyAndPreservesOutcome()
    {
        var firstClock = new SessionTestClock();
        FocusSession session = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            firstClock).Session;
        firstClock.Advance(TimeSpan.FromMinutes(2));
        session = FocusSessionMachineTests.Apply(
            session,
            new ParkSession("Continue from paragraph three"),
            firstClock).Session;
        session = FocusSessionMachineTests.Apply(session, new BeginBreak(), firstClock).Session;
        firstClock.Advance(TimeSpan.FromMinutes(1));
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(session, firstClock);
        var restartedClock = new SessionTestClock(firstClock.GetUtcNow().AddMinutes(10));
        FocusSession restored = FocusSessionMachine.Restore(checkpoint, restartedClock);

        FocusSession resumed = FocusSessionMachineTests.Apply(
            restored,
            new ResumeIncludingAwayTime(TimeSpan.FromMinutes(4)),
            restartedClock).Session;

        Assert.AreEqual(TimeSpan.FromMinutes(2), resumed.CommittedActiveDuration);
        Assert.AreEqual(TimeSpan.FromMinutes(5), resumed.BreakDuration);
        BreakSessionState state = Assert.IsInstanceOfType<BreakSessionState>(resumed.State);
        Assert.AreEqual(SessionOutcome.Parked, state.PriorOutcome);
        Assert.AreEqual("Continue from paragraph three", state.ParkedNextPhysicalAction);
    }

    [TestMethod]
    public void InterruptCommitsObservedSegmentAndRequiresRecovery()
    {
        var clock = new SessionTestClock();
        FocusSession session = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            clock).Session;
        clock.Advance(TimeSpan.FromMinutes(8));

        SessionTransition interrupted = FocusSessionMachineTests.Apply(
            session,
            new InterruptSession(),
            clock);

        Assert.AreEqual(TimeSpan.FromMinutes(8), interrupted.Session.CommittedActiveDuration);
        RecoveryRequiredSessionState state =
            Assert.IsInstanceOfType<RecoveryRequiredSessionState>(interrupted.Session.State);
        Assert.AreEqual(ActiveSessionPhase.Focusing, state.InterruptedPhase);
        Assert.AreEqual(state.CheckpointedAtUtc, state.DetectedAtUtc);
    }

    [TestMethod]
    public void CheckpointDurableStatesRestoreTheirExactStateShape()
    {
        var clock = new SessionTestClock();
        FocusSession ready = FocusSessionMachineTests.CreateSession();
        FocusSession paused = FocusSessionMachineTests.Apply(ready, new StartSession(), clock).Session;
        paused = FocusSessionMachineTests.Apply(paused, new PauseSession(), clock).Session;
        FocusSession completed = FocusSessionMachineTests.Apply(paused, new CompleteSession(), clock).Session;
        FocusSession parkedBase = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            clock).Session;
        FocusSession parked = FocusSessionMachineTests.Apply(
            parkedBase,
            new ParkSession("Retained action"),
            clock).Session;
        FocusSession closed = FocusSessionMachineTests.Apply(
            completed,
            new CloseDay(),
            clock).Session;
        FocusSession[] sessions = [ready, paused, completed, parked, closed];

        foreach (FocusSession expected in sessions)
        {
            SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(expected, clock);
            FocusSession actual = FocusSessionMachine.Restore(checkpoint, clock);
            Assert.AreEqual(expected.State.Kind, actual.State.Kind);
            Assert.AreEqual(expected.CommittedActiveDuration, actual.CommittedActiveDuration);
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.TaskId, actual.TaskId);
        }
    }

    [TestMethod]
    public void CheckpointInvalidStateSpecificShapeIsRejectedClearly()
    {
        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => new SessionCheckpoint(
                new SessionId(Guid.NewGuid()),
                new TaskId(Guid.NewGuid()),
                TimingMode.CountUp,
                TimeSpan.FromMinutes(25),
                TimeSpan.FromMinutes(25),
                SessionCheckpointState.Paused,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                DateTimeOffset.UtcNow));

        Assert.Contains(nameof(SessionCheckpointState.Paused), exception.Message);
    }

    [TestMethod]
    public void CheckpointRejectsApprovedLimitBelowOriginalPlan()
    {
        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => new SessionCheckpoint(
                new SessionId(Guid.NewGuid()),
                new TaskId(Guid.NewGuid()),
                TimingMode.Countdown,
                TimeSpan.FromMinutes(25),
                TimeSpan.FromMinutes(24),
                SessionCheckpointState.Ready,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                DateTimeOffset.UtcNow));

        Assert.Contains("shorter", exception.Message);
    }

    private static FocusSession ReachLanding(SessionTestClock clock)
    {
        FocusSession session = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            clock).Session;
        clock.Advance(TimeSpan.FromMinutes(25));
        session = FocusSessionMachineTests.Apply(session, new RefreshSession(), clock).Session;
        return FocusSessionMachineTests.Apply(session, new BeginLanding(), clock).Session;
    }
}
