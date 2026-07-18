using NowNext.App.Presentation;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;
using NowNext.Core.Tests.Sessions;

namespace NowNext.Core.Tests.Presentation;

[TestClass]
public sealed class FocusControlPolicyTests
{
    [TestMethod]
    public void ForRunningAndPausedSessionsOffersOnlyLegalPauseCommand()
    {
        var clock = new SessionTestClock();
        FocusSession session = CreateSession();
        session = FocusSessionMachine.Apply(session, new StartSession(), clock).Session;

        FocusControlAvailability running = FocusControlPolicy.For(session);
        session = FocusSessionMachine.Apply(session, new PauseSession(), clock).Session;
        FocusControlAvailability paused = FocusControlPolicy.For(session);

        Assert.IsTrue(running.CanPauseOrResume);
        Assert.AreEqual("Pause", running.PauseResumeLabel);
        Assert.IsFalse(running.CanExtend);
        Assert.AreEqual("Resume", paused.PauseResumeLabel);
        Assert.IsFalse(paused.CanBeginLanding);
    }

    [TestMethod]
    public void ForFocusLimitOffersDecisionCommandsButNotPause()
    {
        var clock = new SessionTestClock();
        FocusSession session = CreateSession();
        session = FocusSessionMachine.Apply(session, new StartSession(), clock).Session;
        clock.Advance(TimeSpan.FromMinutes(1));
        session = FocusSessionMachine.Apply(session, new RefreshSession(), clock).Session;

        FocusControlAvailability controls = FocusControlPolicy.For(session);

        Assert.IsFalse(controls.CanPauseOrResume);
        Assert.IsTrue(controls.CanBeginLanding);
        Assert.IsTrue(controls.CanContinueOvertime);
        Assert.IsTrue(controls.CanExtend);
        Assert.IsTrue(controls.CanFinish);
        Assert.IsTrue(controls.CanPark);
    }

    [TestMethod]
    public void ForRestoredActiveCheckpointRequiresRecoveryChoice()
    {
        var firstClock = new SessionTestClock();
        FocusSession running = CreateSession();
        running = FocusSessionMachine.Apply(running, new StartSession(), firstClock).Session;
        firstClock.Advance(TimeSpan.FromSeconds(20));
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(running, firstClock);
        var restartedClock = new SessionTestClock(
            firstClock.GetUtcNow().AddMinutes(2),
            timestamp: 0);
        FocusSession restored = FocusSessionMachine.Restore(checkpoint, restartedClock);

        FocusControlAvailability controls = FocusControlPolicy.For(restored);

        Assert.IsTrue(controls.RequiresRecovery);
        Assert.IsFalse(controls.CanPauseOrResume);
        Assert.IsTrue(controls.CanFinish);
        Assert.IsTrue(controls.CanPark);
    }

    private static FocusSession CreateSession()
    {
        return FocusSessionMachine.Create(
            new SessionId(Guid.NewGuid()),
            new TaskId(Guid.NewGuid()),
            TimingMode.CountUp,
            TimeSpan.FromMinutes(1));
    }
}
