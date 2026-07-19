using NowNext.App;
using NowNext.App.Persistence;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Qualification;

[TestClass]
public sealed class ReleaseCandidateRestartTests
{
    private static readonly DateTimeOffset InitialUtc =
        new(2026, 7, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public ReleaseCandidateRestartTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [DataRow(RestartPhase.Focusing, TimingMode.CountUp)]
    [DataRow(RestartPhase.Paused, TimingMode.Countdown)]
    [DataRow(RestartPhase.LimitReached, TimingMode.CountUp)]
    [DataRow(RestartPhase.Overtime, TimingMode.Countdown)]
    [DataRow(RestartPhase.Landing, TimingMode.CountUp)]
    [DataRow(RestartPhase.Break, TimingMode.Countdown)]
    [Timeout(20_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task RestartEveryDurableActivePhaseNeverInventsTime(
        RestartPhase phase,
        TimingMode timingMode)
    {
        using var database = new TestDatabase();
        var firstClock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create(
            plannedDuration: TimeSpan.FromMinutes(25),
            timingMode: timingMode,
            scheduleType: ScheduleType.Flexible);
        TimeSpan expectedCommitted;

        using (var firstStore = new TodayPlanStore(database.DatabasePath, firstClock))
        using (var firstRuntime = new FocusSessionRuntime(firstStore, firstClock))
        {
            await firstStore.CreateTaskAsync(task, _testContext.CancellationToken);
            await firstRuntime.InitializeAsync(_testContext.CancellationToken);
            await firstRuntime.CreateAsync(
                new SessionId(Guid.NewGuid()),
                task.Id,
                task.TimingMode,
                task.PlannedDuration,
                _testContext.CancellationToken);
            expectedCommitted = await PreparePhaseAsync(
                phase,
                firstRuntime,
                firstClock,
                _testContext.CancellationToken);
        }

        var restartedClock = new ManualTimeProvider(
            firstClock.GetUtcNow().AddHours(3),
            timestamp: 0);
        using var restartedStore = new TodayPlanStore(database.DatabasePath, restartedClock);
        using var restartedRuntime = new FocusSessionRuntime(restartedStore, restartedClock);
        await restartedRuntime.InitializeAsync(_testContext.CancellationToken);
        FocusSession restored = restartedRuntime.Current!;

        Assert.AreEqual(expectedCommitted, restored.CommittedActiveDuration);
        if (phase == RestartPhase.Paused)
        {
            _ = Assert.IsInstanceOfType<PausedSessionState>(restored.State);
        }
        else if (phase == RestartPhase.LimitReached)
        {
            _ = Assert.IsInstanceOfType<LimitReachedSessionState>(restored.State);
        }
        else
        {
            RecoveryRequiredSessionState recovery =
                Assert.IsInstanceOfType<RecoveryRequiredSessionState>(restored.State);
            Assert.AreEqual(ToActivePhase(phase), recovery.InterruptedPhase);
        }
    }

    private static async System.Threading.Tasks.Task<TimeSpan> PreparePhaseAsync(
        RestartPhase phase,
        FocusSessionRuntime runtime,
        ManualTimeProvider clock,
        CancellationToken cancellationToken)
    {
        await runtime.ExecuteAsync(new StartSession(), cancellationToken);
        switch (phase)
        {
            case RestartPhase.Focusing:
                clock.Advance(TimeSpan.FromMinutes(3));
                await runtime.PersistRecoveryCheckpointAsync(cancellationToken);
                return TimeSpan.FromMinutes(3);
            case RestartPhase.Paused:
                clock.Advance(TimeSpan.FromMinutes(3));
                await runtime.ExecuteAsync(new PauseSession(), cancellationToken);
                return TimeSpan.FromMinutes(3);
            case RestartPhase.LimitReached:
                clock.Advance(TimeSpan.FromMinutes(25));
                await runtime.ExecuteAsync(new RefreshSession(), cancellationToken);
                return TimeSpan.FromMinutes(25);
            case RestartPhase.Overtime:
                clock.Advance(TimeSpan.FromMinutes(25));
                await runtime.ExecuteAsync(new RefreshSession(), cancellationToken);
                await runtime.ExecuteAsync(new ContinueOvertime(), cancellationToken);
                clock.Advance(TimeSpan.FromMinutes(2));
                await runtime.PersistRecoveryCheckpointAsync(cancellationToken);
                return TimeSpan.FromMinutes(27);
            case RestartPhase.Landing:
                clock.Advance(TimeSpan.FromMinutes(25));
                await runtime.ExecuteAsync(new RefreshSession(), cancellationToken);
                await runtime.ExecuteAsync(new BeginLanding(), cancellationToken);
                clock.Advance(TimeSpan.FromMinutes(2));
                await runtime.PersistRecoveryCheckpointAsync(cancellationToken);
                return TimeSpan.FromMinutes(27);
            case RestartPhase.Break:
                clock.Advance(TimeSpan.FromMinutes(3));
                await runtime.ExecuteAsync(
                    new ParkSession("Open the next qualification step"),
                    cancellationToken);
                await runtime.ExecuteAsync(new BeginBreak(), cancellationToken);
                clock.Advance(TimeSpan.FromMinutes(2));
                await runtime.PersistRecoveryCheckpointAsync(cancellationToken);
                return TimeSpan.FromMinutes(3);
            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }
    }

    private static ActiveSessionPhase ToActivePhase(RestartPhase phase)
    {
        return phase switch
        {
            RestartPhase.Focusing => ActiveSessionPhase.Focusing,
            RestartPhase.Overtime => ActiveSessionPhase.Overtime,
            RestartPhase.Landing => ActiveSessionPhase.Landing,
            RestartPhase.Break => ActiveSessionPhase.Break,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
        };
    }

    public enum RestartPhase
    {
        Focusing,
        Paused,
        LimitReached,
        Overtime,
        Landing,
        Break,
    }
}
