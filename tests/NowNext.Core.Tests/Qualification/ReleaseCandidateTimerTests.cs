using NowNext.Core.Domain;
using NowNext.Core.Sessions;
using NowNext.Core.Tests.Sessions;

namespace NowNext.Core.Tests.Qualification;

[TestClass]
public sealed class ReleaseCandidateTimerTests
{
    [TestMethod]
    [DataRow(TimingMode.CountUp)]
    [DataRow(TimingMode.Countdown)]
    public void FourteenHourSessionWithoutUiRefreshKeepsAuthoritativeOvertime(
        TimingMode timingMode)
    {
        var clock = new SessionTestClock();
        FocusSession session = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(timingMode),
            new StartSession(),
            clock).Session;
        TimeSpan longRun = TimeSpan.FromHours(14);

        clock.AdvanceMonotonic(longRun);
        SessionView delayedView = FocusSessionMachine.CreateView(session, clock);
        OvertimeTimerReading delayedReading =
            Assert.IsInstanceOfType<OvertimeTimerReading>(delayedView.Timer);
        SessionTransition boundary = FocusSessionMachineTests.Apply(
            session,
            new RefreshSession(),
            clock);
        session = boundary.Session;
        for (int index = 0; index < 1_000; index++)
        {
            SessionTransition repeated = FocusSessionMachineTests.Apply(
                session,
                new RefreshSession(),
                clock);
            Assert.AreEqual(SessionSignal.None, repeated.Signal);
            session = repeated.Session;
        }

        Assert.AreEqual(SessionSignal.FocusLimitReached, boundary.Signal);
        Assert.AreEqual(longRun, session.CommittedActiveDuration);
        Assert.AreEqual(
            longRun - session.ApprovedLimit,
            delayedReading.Overtime);
        Assert.AreEqual(SessionStateKind.Overtime, session.State.Kind);
    }

    [TestMethod]
    public void OneThousandPauseResumeCyclesExcludeEveryPausedInterval()
    {
        var clock = new SessionTestClock();
        FocusSession session = FocusSessionMachineTests.Apply(
            FocusSessionMachineTests.CreateSession(),
            new StartSession(),
            clock).Session;

        for (int index = 0; index < 1_000; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            session = FocusSessionMachineTests.Apply(
                session,
                new PauseSession(),
                clock).Session;
            clock.Advance(TimeSpan.FromSeconds(7));
            session = FocusSessionMachineTests.Apply(
                session,
                new ResumeSession(),
                clock).Session;
        }

        SessionView view = FocusSessionMachine.CreateView(session, clock);
        CountUpTimerReading reading =
            Assert.IsInstanceOfType<CountUpTimerReading>(view.Timer);
        Assert.AreEqual(TimeSpan.FromSeconds(1_000), session.CommittedActiveDuration);
        Assert.AreEqual(TimeSpan.FromSeconds(1_000), reading.Elapsed);
        Assert.AreEqual(SessionStateKind.Focusing, session.State.Kind);
    }
}
