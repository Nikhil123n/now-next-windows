using Microsoft.Data.Sqlite;
using NowNext.App.Persistence;
using NowNext.Core.Domain;
using NowNext.Core.Planning;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Persistence;

[TestClass]
public sealed class WorkdayStoreTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 18, 13, 45, 30, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public WorkdayStoreTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SettingsAndScheduleRevisionRoundTrip()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask dailyWin = TaskAt(9, 0, 30, ScheduleType.Flexible);
        await store.CreateTaskAsync(dailyWin, _testContext.CancellationToken);
        WorkdaySnapshot before = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);

        await store.SaveDaySettingsAsync(
            new DaySettings(before.Plan.Date, new TimeOnly(17, 30), dailyWin.Id),
            _testContext.CancellationToken);
        WorkdaySnapshot after = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);

        Assert.IsNotNull(after.Settings);
        Assert.AreEqual(new TimeOnly(17, 30), after.Settings.ShutdownTime);
        Assert.AreEqual(dailyWin.Id, after.Settings.DailyWinTaskId);
        Assert.AreEqual(before.ScheduleRevision + 1, after.ScheduleRevision);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ApplyAndUndoRepairAreTransactionalAndAudited()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        (DomainTask current, DomainTask movable, DomainTask fixedTask) =
            await CreateRepairPlanAsync(store);
        WorkdaySnapshot snapshot = await ConfigureShutdownAsync(store);
        ScheduleRepairProposal proposal = ProposeRepair(snapshot, current.Id);

        await store.ApplyScheduleRepairAsync(proposal, _testContext.CancellationToken);
        WorkdaySnapshot applied = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);
        DomainTask appliedMovable = applied.Plan.Entries
            .Single(entry => entry.Task.Id == movable.Id).Task;
        DomainTask appliedFixed = applied.Plan.Entries
            .Single(entry => entry.Task.Id == fixedTask.Id).Task;
        long acceptedCount = await ReadScalarAsync(
            database,
            "SELECT COUNT(*) FROM schedule_repairs WHERE undone_at_utc IS NULL;",
            _testContext.CancellationToken);

        Assert.AreEqual(new TimeOnly(11, 0), appliedMovable.PlannedStart);
        Assert.AreEqual(new TimeOnly(10, 30), appliedFixed.PlannedStart);
        Assert.AreEqual(fixedTask.Id, applied.Plan.Entries[1].Task.Id);
        Assert.AreEqual(1L, acceptedCount);

        Assert.IsTrue(await store.UndoLatestScheduleRepairAsync(
            _testContext.CancellationToken));
        WorkdaySnapshot restored = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);
        Assert.AreEqual(
            new TimeOnly(10, 0),
            restored.Plan.Entries.Single(entry => entry.Task.Id == movable.Id).Task.PlannedStart);
        Assert.AreEqual(movable.Id, restored.Plan.Entries[1].Task.Id);
        Assert.IsFalse(await store.UndoLatestScheduleRepairAsync(
            _testContext.CancellationToken));
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task StaleOrCancelledRepairDoesNotMutatePlan()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        (DomainTask current, DomainTask movable, _) = await CreateRepairPlanAsync(store);
        WorkdaySnapshot snapshot = await ConfigureShutdownAsync(store);
        ScheduleRepairProposal proposal = ProposeRepair(snapshot, current.Id);
        await store.SaveDaySettingsAsync(
            new DaySettings(snapshot.Plan.Date, new TimeOnly(17, 0), movable.Id),
            _testContext.CancellationToken);

        InvalidOperationException stale =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await store.ApplyScheduleRepairAsync(
                    proposal,
                    _testContext.CancellationToken));
        Assert.Contains("schedule changed", stale.Message);

        WorkdaySnapshot fresh = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);
        ScheduleRepairProposal freshProposal = ProposeRepair(fresh, current.Id);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await store.ApplyScheduleRepairAsync(
                freshProposal,
                cancellation.Token));
        WorkdaySnapshot unchanged = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);

        Assert.AreEqual(
            new TimeOnly(10, 0),
            unchanged.Plan.Entries.Single(entry => entry.Task.Id == movable.Id).Task.PlannedStart);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task StaleUndoRejectsLaterTaskChange()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        (DomainTask current, DomainTask movable, _) = await CreateRepairPlanAsync(store);
        WorkdaySnapshot snapshot = await ConfigureShutdownAsync(store);
        await store.ApplyScheduleRepairAsync(
            ProposeRepair(snapshot, current.Id),
            _testContext.CancellationToken);
        WorkdaySnapshot applied = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);
        DomainTask changed = CopyTask(
            applied.Plan.Entries.Single(entry => entry.Task.Id == movable.Id).Task,
            plannedStart: new TimeOnly(11, 15));
        await store.EditTaskAsync(changed, _testContext.CancellationToken);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await store.UndoLatestScheduleRepairAsync(
                    _testContext.CancellationToken));

        Assert.Contains("cannot be undone", exception.Message);
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);
        Assert.AreEqual(
            new TimeOnly(11, 15),
            plan.Entries.Single(entry => entry.Task.Id == movable.Id).Task.PlannedStart);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ShutdownUsesRetainedLedgerAndClosureSurvivesRestart()
    {
        using var database = new TestDatabase();
        DomainTask completed = TestTaskFactory.Create(
            plannedStart: new TimeOnly(9, 0),
            plannedDuration: TimeSpan.FromMinutes(30));
        DomainTask unfinished = TestTaskFactory.Create(
            plannedStart: new TimeOnly(10, 0),
            plannedDuration: TimeSpan.FromMinutes(45),
            scheduleType: ScheduleType.Flexible,
            importance: TaskImportance.Important,
            nextPhysicalAction: "Open the next section");
        using (var store = CreateStore(database))
        {
            await store.CreateTaskAsync(completed, _testContext.CancellationToken);
            await store.CreateTaskAsync(unfinished, _testContext.CancellationToken);
            await store.SaveDaySettingsAsync(
                new DaySettings(new DateOnly(2026, 7, 18), new TimeOnly(17, 0), completed.Id),
                _testContext.CancellationToken);
            await store.SaveCurrentSessionAsync(
                CompletedCheckpoint(completed, TimeSpan.FromMinutes(35), TimeSpan.FromMinutes(5)),
                _testContext.CancellationToken);
            ShutdownSummary summary = await store.CreateShutdownSummaryAsync(
                _testContext.CancellationToken);

            Assert.AreEqual(TimeSpan.FromMinutes(75), summary.TotalPlannedDuration);
            Assert.AreEqual(TimeSpan.FromMinutes(35), summary.TotalActualDuration);
            Assert.AreEqual(DailyWinStatus.Completed, summary.DailyWinStatus);
            Assert.AreEqual(unfinished.Id, summary.NextUnfinishedTaskId);
            Assert.AreEqual("Open the next section", summary.NextPhysicalAction);
            await store.CloseDayAsync(summary, cancellationToken: _testContext.CancellationToken);
        }

        using var restarted = CreateStore(database);
        DayClosure? closure = await restarted.LoadDayClosureAsync(
            _testContext.CancellationToken);
        Assert.IsNotNull(closure);
        Assert.AreEqual(TimeSpan.FromMinutes(35), closure.TotalActualDuration);
        Assert.AreEqual(DailyWinStatus.Completed, closure.DailyWinStatus);
        Assert.HasCount(1, closure.Items);

        InvalidOperationException closed =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await restarted.CreateTaskAsync(
                    TestTaskFactory.Create(),
                    _testContext.CancellationToken));
        Assert.Contains("already closed", closed.Message);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ShutdownWithoutExplicitProtectedTimeIsRejected()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        await store.CreateTaskAsync(
            TestTaskFactory.Create(),
            _testContext.CancellationToken);
        ShutdownSummary summary = await store.CreateShutdownSummaryAsync(
            _testContext.CancellationToken);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await store.CloseDayAsync(
                    summary,
                    cancellationToken: _testContext.CancellationToken));

        Assert.Contains("protected shutdown", exception.Message);
        Assert.IsNull(await store.LoadDayClosureAsync(_testContext.CancellationToken));
    }

    private async System.Threading.Tasks.Task<(DomainTask Current, DomainTask Movable, DomainTask Fixed)>
        CreateRepairPlanAsync(TodayPlanStore store)
    {
        DomainTask current = TaskAt(9, 0, 60, ScheduleType.Flexible);
        DomainTask movable = TaskAt(10, 0, 45, ScheduleType.Flexible);
        DomainTask fixedTask = TaskAt(10, 30, 30, ScheduleType.Fixed);
        await store.CreateTaskAsync(current, _testContext.CancellationToken);
        await store.CreateTaskAsync(movable, _testContext.CancellationToken);
        await store.CreateTaskAsync(fixedTask, _testContext.CancellationToken);
        return (current, movable, fixedTask);
    }

    private async System.Threading.Tasks.Task<WorkdaySnapshot> ConfigureShutdownAsync(
        TodayPlanStore store)
    {
        WorkdaySnapshot snapshot = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);
        await store.SaveDaySettingsAsync(
            new DaySettings(snapshot.Plan.Date, new TimeOnly(17, 0)),
            _testContext.CancellationToken);
        return await store.LoadWorkdaySnapshotAsync(_testContext.CancellationToken);
    }

    private static ScheduleRepairProposal ProposeRepair(
        WorkdaySnapshot snapshot,
        TaskId currentTaskId)
    {
        Assert.IsNotNull(snapshot.Settings);
        return ScheduleRepairEngine.Propose(new ScheduleRepairRequest(
            new ScheduleRepairId(Guid.NewGuid()),
            snapshot.ScheduleRevision,
            snapshot.Plan,
            new TimeOnly(9, 50),
            snapshot.Settings.ShutdownTime,
            new ScheduleRepairTrigger(
                ScheduleRepairTriggerKind.CurrentTime,
                FixedNow),
            currentTaskId,
            TimeSpan.FromMinutes(20)));
    }

    private static SessionCheckpoint CompletedCheckpoint(
        DomainTask task,
        TimeSpan activeDuration,
        TimeSpan landingDuration)
    {
        return new SessionCheckpoint(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            task.PlannedDuration,
            SessionCheckpointState.Completed,
            activeDuration,
            landingDuration,
            TimeSpan.Zero,
            FixedNow,
            startedAtUtc: FixedNow.Subtract(activeDuration),
            completedAtUtc: FixedNow);
    }

    private static DomainTask TaskAt(
        int hour,
        int minute,
        double durationMinutes,
        ScheduleType scheduleType)
    {
        return TestTaskFactory.Create(
            plannedStart: new TimeOnly(hour, minute),
            plannedDuration: TimeSpan.FromMinutes(durationMinutes),
            scheduleType: scheduleType);
    }

    private static DomainTask CopyTask(DomainTask source, TimeOnly plannedStart)
    {
        return new DomainTask(
            source.Id,
            source.FullTitle,
            source.ShortFocusLabel,
            source.DefinitionOfDone,
            source.FirstPhysicalAction,
            source.NextPhysicalAction,
            plannedStart,
            source.PlannedDuration,
            source.TimingMode,
            source.ScheduleType,
            source.Importance,
            source.State);
    }

    private static TodayPlanStore CreateStore(TestDatabase database)
    {
        return new TodayPlanStore(database.DatabasePath, new FixedTimeProvider(FixedNow));
    }

    private static async System.Threading.Tasks.Task<long> ReadScalarAsync(
        TestDatabase database,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
