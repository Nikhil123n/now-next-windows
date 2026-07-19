using Microsoft.Data.Sqlite;
using NowNext.App.Persistence;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Persistence;

[TestClass]
public sealed class CurrentSessionStoreTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 18, 13, 45, 30, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public CurrentSessionStoreTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [DataRow(TimingMode.CountUp)]
    [DataRow(TimingMode.Countdown)]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SaveAndLoadReadyCheckpointRoundTripsExactly(
        TimingMode timingMode)
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = await CreateTaskAsync(store, timingMode);
        SessionCheckpoint expected = CreateCheckpoint(task, timingMode: timingMode);

        await store.SaveCurrentSessionAsync(expected, _testContext.CancellationToken);
        SessionCheckpoint? actual =
            await store.LoadCurrentSessionAsync(_testContext.CancellationToken);

        Assert.IsNotNull(actual);
        AssertCheckpointEqual(expected, actual);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SaveKeyDurableStatesRoundTripsStateSpecificFields()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = await CreateTaskAsync(store, TimingMode.CountUp);
        SessionId sessionId = new(Guid.NewGuid());
        SessionCheckpoint[] checkpoints =
        [
            CreateCheckpoint(
                task,
                id: sessionId,
                state: SessionCheckpointState.Paused,
                committedActiveDuration: TimeSpan.FromMinutes(4),
                resumePhase: ActiveSessionPhase.Focusing,
                startedAtUtc: FixedNow.AddMinutes(-4)),
            CreateCheckpoint(
                task,
                id: sessionId,
                state: SessionCheckpointState.LimitReached,
                committedActiveDuration: task.PlannedDuration,
                boundary: SessionBoundary.FocusLimit,
                startedAtUtc: FixedNow.AddMinutes(-45)),
            CreateCheckpoint(
                task,
                id: sessionId,
                state: SessionCheckpointState.RecoveryRequired,
                committedActiveDuration: TimeSpan.FromMinutes(48),
                recoveryPhase: ActiveSessionPhase.Overtime,
                startedAtUtc: FixedNow.AddMinutes(-50)),
            CreateCheckpoint(
                task,
                id: sessionId,
                state: SessionCheckpointState.Completed,
                committedActiveDuration: TimeSpan.FromMinutes(48),
                startedAtUtc: FixedNow.AddMinutes(-50),
                completedAtUtc: FixedNow),
            CreateCheckpoint(
                task,
                id: sessionId,
                state: SessionCheckpointState.Parked,
                committedActiveDuration: TimeSpan.FromMinutes(48),
                startedAtUtc: FixedNow.AddMinutes(-50),
                parkedAtUtc: FixedNow,
                parkedNextPhysicalAction: "Open the next section"),
            CreateCheckpoint(
                task,
                id: sessionId,
                state: SessionCheckpointState.Abandoned,
                committedActiveDuration: TimeSpan.FromMinutes(48),
                startedAtUtc: FixedNow.AddMinutes(-50),
                abandonedAtUtc: FixedNow),
            CreateCheckpoint(
                task,
                id: sessionId,
                state: SessionCheckpointState.DayClosed,
                committedActiveDuration: TimeSpan.FromMinutes(48),
                priorOutcome: SessionOutcome.Parked,
                startedAtUtc: FixedNow.AddMinutes(-50),
                parkedAtUtc: FixedNow.AddMinutes(-1),
                dayClosedAtUtc: FixedNow,
                parkedNextPhysicalAction: "Open the next section"),
        ];

        foreach (SessionCheckpoint expected in checkpoints)
        {
            await store.SaveCurrentSessionAsync(expected, _testContext.CancellationToken);
            SessionCheckpoint? actual =
                await store.LoadCurrentSessionAsync(_testContext.CancellationToken);

            Assert.IsNotNull(actual);
            AssertCheckpointEqual(expected, actual);
        }
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SaveCheckpointUpdatesTaskLifecycleInSameTransaction()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = await CreateTaskAsync(store, TimingMode.CountUp);
        SessionId sessionId = new(Guid.NewGuid());
        SessionCheckpoint paused = CreateCheckpoint(
            task,
            id: sessionId,
            state: SessionCheckpointState.Paused,
            committedActiveDuration: TimeSpan.FromMinutes(2),
            resumePhase: ActiveSessionPhase.Focusing,
            startedAtUtc: FixedNow.AddMinutes(-2));
        SessionCheckpoint parked = CreateCheckpoint(
            task,
            id: sessionId,
            state: SessionCheckpointState.Parked,
            committedActiveDuration: TimeSpan.FromMinutes(2),
            startedAtUtc: FixedNow.AddMinutes(-2),
            parkedAtUtc: FixedNow,
            parkedNextPhysicalAction: "Reopen the draft");
        SessionCheckpoint completed = CreateCheckpoint(
            task,
            id: sessionId,
            state: SessionCheckpointState.Completed,
            committedActiveDuration: TimeSpan.FromMinutes(2),
            startedAtUtc: FixedNow.AddMinutes(-2),
            completedAtUtc: FixedNow);
        SessionCheckpoint interruptedBreak = CreateCheckpoint(
            task,
            id: sessionId,
            state: SessionCheckpointState.RecoveryRequired,
            committedActiveDuration: TimeSpan.FromMinutes(2),
            breakDuration: TimeSpan.FromMinutes(1),
            recoveryPhase: ActiveSessionPhase.Break,
            priorOutcome: SessionOutcome.Completed,
            startedAtUtc: FixedNow.AddMinutes(-3),
            completedAtUtc: FixedNow.AddMinutes(-1),
            breakPlan: new BreakPlan(
                TimeSpan.FromMinutes(5),
                new BreakPrompt(BreakPromptKind.Water)));

        await store.SaveCurrentSessionAsync(paused, _testContext.CancellationToken);
        DomainTask activeTask = await LoadOnlyTaskAsync(store);
        await store.SaveCurrentSessionAsync(completed, _testContext.CancellationToken);
        DomainTask completedTask = await LoadOnlyTaskAsync(store);
        await store.SaveCurrentSessionAsync(interruptedBreak, _testContext.CancellationToken);
        DomainTask breakTask = await LoadOnlyTaskAsync(store);
        await store.SaveCurrentSessionAsync(parked, _testContext.CancellationToken);
        DomainTask parkedTask = await LoadOnlyTaskAsync(store);

        Assert.AreEqual(TaskState.Active, activeTask.State);
        Assert.AreEqual(TaskState.Completed, completedTask.State);
        Assert.AreEqual(TaskState.Completed, breakTask.State);
        Assert.AreEqual(TaskState.Parked, parkedTask.State);
        Assert.AreEqual("Reopen the draft", parkedTask.NextPhysicalAction);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SaveDifferentSessionWhileCurrentIsUnresolvedRollsBack()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask firstTask = await CreateTaskAsync(store, TimingMode.CountUp);
        DomainTask secondTask = await CreateTaskAsync(store, TimingMode.Countdown);
        SessionCheckpoint original = CreateCheckpoint(firstTask);
        SessionCheckpoint replacement = CreateCheckpoint(secondTask);
        await store.SaveCurrentSessionAsync(original, _testContext.CancellationToken);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await store.SaveCurrentSessionAsync(
                    replacement,
                    _testContext.CancellationToken));
        SessionCheckpoint? persisted =
            await store.LoadCurrentSessionAsync(_testContext.CancellationToken);

        Assert.Contains("must be resolved", exception.Message);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(original.Id, persisted.Id);
        Assert.AreEqual(original.TaskId, persisted.TaskId);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task DeleteTaskWithUnresolvedSessionIsRejectedWithoutMutation()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = await CreateTaskAsync(store, TimingMode.CountUp);
        await store.SaveCurrentSessionAsync(
            CreateCheckpoint(task),
            _testContext.CancellationToken);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await store.DeleteTaskAsync(task.Id, _testContext.CancellationToken));
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);

        Assert.Contains("unresolved focus session", exception.Message);
        Assert.HasCount(1, plan.Entries);
        Assert.AreEqual(task.Id, plan.Entries[0].Task.Id);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task PreCancelledSaveDoesNotCreateCheckpointOrUpdateTask()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = await CreateTaskAsync(store, TimingMode.CountUp);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await store.SaveCurrentSessionAsync(
                CreateCheckpoint(
                    task,
                    state: SessionCheckpointState.Paused,
                    resumePhase: ActiveSessionPhase.Focusing,
                    startedAtUtc: FixedNow),
                cancellation.Token));
        SessionCheckpoint? persisted =
            await store.LoadCurrentSessionAsync(_testContext.CancellationToken);
        DomainTask persistedTask = await LoadOnlyTaskAsync(store);

        Assert.IsNull(persisted);
        Assert.AreEqual(TaskState.Planned, persistedTask.State);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CorruptStoredSessionStateThrowsInvalidDataFailure()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = await CreateTaskAsync(store, TimingMode.CountUp);
        await store.SaveCurrentSessionAsync(
            CreateCheckpoint(task),
            _testContext.CancellationToken);
        await CorruptSessionStateAsync(database, _testContext.CancellationToken);

        InvalidDataException exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await store.LoadCurrentSessionAsync(_testContext.CancellationToken));

        Assert.Contains(nameof(SessionCheckpointState), exception.Message);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task RestartLoadsOnlyCommittedRecoveryDuration()
    {
        using var database = new TestDatabase();
        DomainTask task;
        SessionCheckpoint expected;
        using (var firstStore = CreateStore(database))
        {
            task = await CreateTaskAsync(firstStore, TimingMode.Countdown);
            expected = CreateCheckpoint(
                task,
                timingMode: TimingMode.Countdown,
                state: SessionCheckpointState.RecoveryRequired,
                committedActiveDuration: TimeSpan.FromMinutes(12),
                recoveryPhase: ActiveSessionPhase.Focusing,
                startedAtUtc: FixedNow.AddMinutes(-12));
            await firstStore.SaveCurrentSessionAsync(expected, _testContext.CancellationToken);
        }

        using var restartedStore = new TodayPlanStore(
            database.DatabasePath,
            new FixedTimeProvider(FixedNow.AddHours(6)));
        SessionCheckpoint? actual =
            await restartedStore.LoadCurrentSessionAsync(_testContext.CancellationToken);

        Assert.IsNotNull(actual);
        Assert.AreEqual(SessionCheckpointState.RecoveryRequired, actual.State);
        Assert.AreEqual(TimeSpan.FromMinutes(12), actual.CommittedActiveDuration);
        Assert.AreEqual(expected.CheckpointedAtUtc, actual.CheckpointedAtUtc);
    }

    private async System.Threading.Tasks.Task<DomainTask> CreateTaskAsync(
        TodayPlanStore store,
        TimingMode timingMode)
    {
        DomainTask task = TestTaskFactory.Create(timingMode: timingMode);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        return task;
    }

    private async System.Threading.Tasks.Task<DomainTask> LoadOnlyTaskAsync(TodayPlanStore store)
    {
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);
        return Assert.ContainsSingle(plan.Entries).Task;
    }

    private static TodayPlanStore CreateStore(TestDatabase database)
    {
        return new TodayPlanStore(database.DatabasePath, new FixedTimeProvider(FixedNow));
    }

    private static SessionCheckpoint CreateCheckpoint(
        DomainTask task,
        SessionId? id = null,
        TimingMode? timingMode = null,
        SessionCheckpointState state = SessionCheckpointState.Ready,
        TimeSpan? committedActiveDuration = null,
        TimeSpan? landingDuration = null,
        TimeSpan? breakDuration = null,
        ActiveSessionPhase? resumePhase = null,
        SessionBoundary? boundary = null,
        ActiveSessionPhase? recoveryPhase = null,
        SessionOutcome? priorOutcome = null,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? completedAtUtc = null,
        DateTimeOffset? parkedAtUtc = null,
        DateTimeOffset? dayClosedAtUtc = null,
        string? parkedNextPhysicalAction = null,
        DateTimeOffset? abandonedAtUtc = null,
        BreakPlan? breakPlan = null)
    {
        return new SessionCheckpoint(
            id ?? new SessionId(Guid.NewGuid()),
            task.Id,
            timingMode ?? task.TimingMode,
            task.PlannedDuration,
            task.PlannedDuration,
            state,
            committedActiveDuration ?? TimeSpan.Zero,
            landingDuration ?? TimeSpan.Zero,
            breakDuration ?? TimeSpan.Zero,
            FixedNow,
            resumePhase,
            boundary,
            recoveryPhase,
            priorOutcome,
            startedAtUtc,
            completedAtUtc,
            parkedAtUtc,
            dayClosedAtUtc,
            parkedNextPhysicalAction,
            abandonedAtUtc,
            breakPlan);
    }

    private static void AssertCheckpointEqual(
        SessionCheckpoint expected,
        SessionCheckpoint actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.TaskId, actual.TaskId);
        Assert.AreEqual(expected.TimingMode, actual.TimingMode);
        Assert.AreEqual(expected.OriginalPlannedDuration, actual.OriginalPlannedDuration);
        Assert.AreEqual(expected.ApprovedLimit, actual.ApprovedLimit);
        Assert.AreEqual(expected.State, actual.State);
        Assert.AreEqual(expected.CommittedActiveDuration, actual.CommittedActiveDuration);
        Assert.AreEqual(expected.LandingDuration, actual.LandingDuration);
        Assert.AreEqual(expected.BreakDuration, actual.BreakDuration);
        Assert.AreEqual(expected.CheckpointedAtUtc, actual.CheckpointedAtUtc);
        Assert.AreEqual(expected.ResumePhase, actual.ResumePhase);
        Assert.AreEqual(expected.Boundary, actual.Boundary);
        Assert.AreEqual(expected.RecoveryPhase, actual.RecoveryPhase);
        Assert.AreEqual(expected.PriorOutcome, actual.PriorOutcome);
        Assert.AreEqual(expected.StartedAtUtc, actual.StartedAtUtc);
        Assert.AreEqual(expected.CompletedAtUtc, actual.CompletedAtUtc);
        Assert.AreEqual(expected.ParkedAtUtc, actual.ParkedAtUtc);
        Assert.AreEqual(expected.DayClosedAtUtc, actual.DayClosedAtUtc);
        Assert.AreEqual(expected.ParkedNextPhysicalAction, actual.ParkedNextPhysicalAction);
        Assert.AreEqual(expected.AbandonedAtUtc, actual.AbandonedAtUtc);
        Assert.AreEqual(expected.BreakPlan, actual.BreakPlan);
    }

    private static async System.Threading.Tasks.Task CorruptSessionStateAsync(
        TestDatabase database,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE current_session_checkpoint SET session_state = 'Unknown' WHERE slot = 1;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
