using NowNext.App;
using NowNext.App.Persistence;
using NowNext.Core.Domain;
using NowNext.Core.Planning;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Qualification;

[TestClass]
public sealed class ReleaseCandidateJourneyTests
{
    private static readonly DateTimeOffset InitialUtc =
        new(2026, 7, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public ReleaseCandidateJourneyTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(20_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CompletePersistedP0JourneySurvivesShutdownRestart()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask countUp = CreateTask(
            "Count-up qualification",
            "Count up",
            new TimeOnly(9, 0),
            60,
            TimingMode.CountUp,
            ScheduleType.Flexible,
            TaskImportance.Important);
        DomainTask countdown = CreateTask(
            "Countdown qualification",
            "Count down",
            new TimeOnly(10, 45),
            45,
            TimingMode.Countdown,
            ScheduleType.Flexible,
            TaskImportance.Normal);
        DomainTask movable = CreateTask(
            "Movable qualification",
            "Move later",
            new TimeOnly(11, 30),
            20,
            TimingMode.CountUp,
            ScheduleType.Flexible,
            TaskImportance.Normal);
        DomainTask fixedCommitment = CreateTask(
            "Fixed qualification",
            "Fixed work",
            new TimeOnly(12, 0),
            30,
            TimingMode.Countdown,
            ScheduleType.Fixed,
            TaskImportance.Important);

        using (var store = new TodayPlanStore(database.DatabasePath, clock))
        using (var runtime = new FocusSessionRuntime(store, clock))
        {
            await store.CreateTaskAsync(countUp, _testContext.CancellationToken);
            await store.CreateTaskAsync(countdown, _testContext.CancellationToken);
            await store.CreateTaskAsync(movable, _testContext.CancellationToken);
            await store.CreateTaskAsync(fixedCommitment, _testContext.CancellationToken);
            TodayPlan created = await store.LoadTodayPlanAsync(
                _testContext.CancellationToken);
            Assert.AreEqual(TimingMode.CountUp, created.Entries[0].Task.TimingMode);
            Assert.AreEqual(TimingMode.Countdown, created.Entries[1].Task.TimingMode);
            await store.SaveDaySettingsAsync(
                new DaySettings(created.Date, new TimeOnly(17, 0), countdown.Id),
                _testContext.CancellationToken);
            await runtime.InitializeAsync(_testContext.CancellationToken);

            await runtime.CreateAsync(
                new SessionId(Guid.NewGuid()),
                countUp.Id,
                countUp.TimingMode,
                countUp.PlannedDuration,
                _testContext.CancellationToken);
            _ = Assert.IsInstanceOfType<ReadySessionState>(runtime.Current!.State);
            await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(15));
            await runtime.ExecuteAsync(new RefreshSession(), _testContext.CancellationToken);
            await runtime.ExecuteAsync(new PauseSession(), _testContext.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(30));
            await runtime.ExecuteAsync(new ResumeSession(), _testContext.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(45));
            SessionTransition focusBoundary = await runtime.ExecuteAsync(
                new RefreshSession(),
                _testContext.CancellationToken);
            Assert.AreEqual(SessionSignal.FocusLimitReached, focusBoundary.Signal);
            await runtime.ExecuteAsync(new ContinueOvertime(), _testContext.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(2));
            await runtime.ExecuteAsync(new RefreshSession(), _testContext.CancellationToken);
            OvertimeTimerReading overtime = Assert.IsInstanceOfType<OvertimeTimerReading>(
                runtime.GetCurrentView().Timer);
            Assert.AreEqual(TimeSpan.FromMinutes(2), overtime.Overtime);

            await runtime.ExecuteAsync(new BeginLanding(), _testContext.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(5));
            SessionTransition landingBoundary = await runtime.ExecuteAsync(
                new RefreshSession(),
                _testContext.CancellationToken);
            Assert.AreEqual(SessionSignal.LandingLimitReached, landingBoundary.Signal);
            const string nextAction = "Open the qualification checklist";
            await runtime.ExecuteAsync(
                new ParkSession(nextAction, "Resume from the restart section"),
                _testContext.CancellationToken);
            ContextCapsule? capsule = await store.LoadLatestContextCapsuleAsync(
                countUp.Id,
                _testContext.CancellationToken);
            Assert.IsNotNull(capsule);
            Assert.AreEqual(nextAction, capsule.NextPhysicalAction);

            await runtime.ExecuteAsync(
                new BeginBreak(new BreakPlan(
                    TimeSpan.FromMinutes(5),
                    new BreakPrompt(BreakPromptKind.Water))),
                _testContext.CancellationToken);
            clock.Advance(TimeSpan.FromMinutes(5));
            SessionTransition breakBoundary = await runtime.ExecuteAsync(
                new RefreshSession(),
                _testContext.CancellationToken);
            Assert.AreEqual(SessionSignal.BreakLimitReached, breakBoundary.Signal);
            await runtime.ExecuteAsync(new EndBreak(), _testContext.CancellationToken);
            _ = Assert.IsInstanceOfType<ParkedSessionState>(runtime.Current!.State);

            await runtime.CreateAsync(
                new SessionId(Guid.NewGuid()),
                countdown.Id,
                countdown.TimingMode,
                countdown.PlannedDuration,
                _testContext.CancellationToken);
            _ = Assert.IsInstanceOfType<ReadySessionState>(runtime.Current!.State);
            await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
            Assert.AreEqual(
                countdown.PlannedDuration,
                Assert.IsInstanceOfType<CountdownTimerReading>(
                    runtime.GetCurrentView().Timer).Remaining);
            clock.Advance(TimeSpan.FromMinutes(45));
            await runtime.ExecuteAsync(new RefreshSession(), _testContext.CancellationToken);
            await runtime.ExecuteAsync(
                new ExtendSession(TimeSpan.FromMinutes(10)),
                _testContext.CancellationToken);
            Assert.AreEqual(TimeSpan.FromMinutes(55), runtime.Current!.ApprovedLimit);

            WorkdaySnapshot beforeRepair = await store.LoadWorkdaySnapshotAsync(
                _testContext.CancellationToken);
            Assert.IsNotNull(beforeRepair.Settings);
            TimeOnly observedTime = TimeOnly.FromDateTime(clock.GetUtcNow().DateTime);
            ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(
                new ScheduleRepairRequest(
                    new ScheduleRepairId(Guid.NewGuid()),
                    beforeRepair.ScheduleRevision,
                    beforeRepair.Plan,
                    observedTime,
                    beforeRepair.Settings.ShutdownTime,
                    new ScheduleRepairTrigger(
                        ScheduleRepairTriggerKind.SessionExtended,
                        clock.GetUtcNow(),
                        TimeSpan.FromMinutes(10)),
                    countdown.Id,
                    TimeSpan.FromMinutes(10)));
            Assert.AreEqual(ScheduleRepairStatus.RequiresApproval, proposal.Status);
            Assert.IsNotEmpty(proposal.Moves);
            await store.ApplyScheduleRepairAsync(
                proposal,
                _testContext.CancellationToken);
            Assert.IsTrue(await store.UndoLatestScheduleRepairAsync(
                _testContext.CancellationToken));
            TodayPlan repairedThenUndone = await store.LoadTodayPlanAsync(
                _testContext.CancellationToken);
            Assert.AreEqual(
                movable.PlannedStart,
                repairedThenUndone.Entries
                    .Single(entry => entry.Task.Id == movable.Id)
                    .Task.PlannedStart);
            Assert.AreEqual(
                fixedCommitment.PlannedStart,
                repairedThenUndone.Entries
                    .Single(entry => entry.Task.Id == fixedCommitment.Id)
                    .Task.PlannedStart);

            clock.Advance(TimeSpan.FromMinutes(10));
            await runtime.ExecuteAsync(new RefreshSession(), _testContext.CancellationToken);
            await runtime.ExecuteAsync(new CompleteSession(), _testContext.CancellationToken);
            ShutdownSummary summary = await store.CreateShutdownSummaryAsync(
                _testContext.CancellationToken);
            Assert.AreEqual(DailyWinStatus.Completed, summary.DailyWinStatus);
            Assert.Contains(
                countdown.Id,
                summary.Completed.Select(item => item.TaskId));
            DayClosure closure = await runtime.CloseDayAsync(
                summary,
                new NoOpKeepAwakeController(),
                _testContext.CancellationToken);
            Assert.AreEqual(created.Date, closure.Date);
            _ = Assert.IsInstanceOfType<DayClosedSessionState>(runtime.Current!.State);
        }

        clock.Advance(TimeSpan.FromHours(8));
        using var restartedStore = new TodayPlanStore(database.DatabasePath, clock);
        using var restartedRuntime = new FocusSessionRuntime(restartedStore, clock);
        await restartedRuntime.InitializeAsync(_testContext.CancellationToken);
        DayClosure? restoredClosure = await restartedStore.LoadDayClosureAsync(
            _testContext.CancellationToken);
        ContextCapsule? restoredCapsule = await restartedStore.LoadLatestContextCapsuleAsync(
            countUp.Id,
            _testContext.CancellationToken);

        Assert.IsNotNull(restoredClosure);
        Assert.IsNotNull(restoredCapsule);
        Assert.AreEqual("Open the qualification checklist", restoredCapsule.NextPhysicalAction);
        _ = Assert.IsInstanceOfType<DayClosedSessionState>(restartedRuntime.Current!.State);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await restartedStore.CreateTaskAsync(
                TestTaskFactory.Create(),
                _testContext.CancellationToken));
    }

    private static DomainTask CreateTask(
        string title,
        string focusLabel,
        TimeOnly plannedStart,
        double plannedMinutes,
        TimingMode timingMode,
        ScheduleType scheduleType,
        TaskImportance importance)
    {
        return TestTaskFactory.Create(
            fullTitle: title,
            shortFocusLabel: focusLabel,
            definitionOfDone: "The qualification step is complete",
            firstPhysicalAction: "Open the qualification checklist",
            plannedStart: plannedStart,
            plannedDuration: TimeSpan.FromMinutes(plannedMinutes),
            timingMode: timingMode,
            scheduleType: scheduleType,
            importance: importance);
    }
}
