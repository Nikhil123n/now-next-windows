using Microsoft.Data.Sqlite;
using NowNext.App.Persistence;
using NowNext.Core.Domain;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Persistence;

[TestClass]
public sealed class TodayPlanStoreTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 18, 13, 45, 30, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public TodayPlanStoreTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CreateAndLoadAllFieldsReturnsExactValues()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask countUpTask = TestTaskFactory.Create(
            fullTitle: "Count up task",
            shortFocusLabel: "Count up",
            definitionOfDone: "Count up complete",
            firstPhysicalAction: "Start count up",
            nextPhysicalAction: null,
            plannedStart: new TimeOnly(8, 5, 4, 321),
            plannedDuration: TimeSpan.FromMinutes(35),
            timingMode: TimingMode.CountUp,
            scheduleType: ScheduleType.Fixed,
            importance: TaskImportance.Normal,
            state: TaskState.Planned);
        DomainTask countdownTask = TestTaskFactory.Create(
            fullTitle: "Countdown task",
            shortFocusLabel: "Countdown",
            definitionOfDone: "Countdown complete",
            firstPhysicalAction: "Start countdown",
            nextPhysicalAction: "Resume from checkpoint",
            plannedStart: new TimeOnly(15, 42, 3, 654),
            plannedDuration: TimeSpan.FromTicks(12_345_678_901),
            timingMode: TimingMode.Countdown,
            scheduleType: ScheduleType.Flexible,
            importance: TaskImportance.Important,
            state: TaskState.Parked);

        await store.CreateTaskAsync(countUpTask, _testContext.CancellationToken);
        await store.CreateTaskAsync(countdownTask, _testContext.CancellationToken);

        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);

        Assert.AreEqual(new DateOnly(2026, 7, 18), plan.Date);
        Assert.HasCount(2, plan.Entries);
        AssertTaskEqual(countUpTask, plan.Entries[0].Task);
        Assert.AreEqual(0, plan.Entries[0].Position);
        AssertTaskEqual(countdownTask, plan.Entries[1].Task);
        Assert.AreEqual(1, plan.Entries[1].Position);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CreateEveryLifecycleStateRoundTrips()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        TaskState[] states = Enum.GetValues<TaskState>();

        foreach (TaskState state in states)
        {
            DomainTask task = TestTaskFactory.Create(
                fullTitle: $"State {state}",
                nextPhysicalAction: state == TaskState.Parked ? "Resume work" : null,
                state: state);
            await store.CreateTaskAsync(task, _testContext.CancellationToken);
        }

        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);

        Assert.HasCount(states.Length, plan.Entries);
        for (int index = 0; index < states.Length; index++)
        {
            Assert.AreEqual(states[index], plan.Entries[index].Task.State);
        }
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task EditExistingTaskPreservesIdentityAndOrder()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask first = TestTaskFactory.Create(fullTitle: "First");
        DomainTask second = TestTaskFactory.Create(fullTitle: "Second");
        await store.CreateTaskAsync(first, _testContext.CancellationToken);
        await store.CreateTaskAsync(second, _testContext.CancellationToken);
        DomainTask edited = TestTaskFactory.Create(
            id: first.Id,
            fullTitle: "Edited first",
            shortFocusLabel: "Edited",
            definitionOfDone: "Edited complete",
            firstPhysicalAction: "Edit now",
            nextPhysicalAction: "Continue editing",
            plannedStart: new TimeOnly(11, 20),
            plannedDuration: TimeSpan.FromMinutes(20),
            timingMode: TimingMode.Countdown,
            scheduleType: ScheduleType.Flexible,
            importance: TaskImportance.Important,
            state: TaskState.Active);

        await store.EditTaskAsync(edited, _testContext.CancellationToken);
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);

        AssertTaskEqual(edited, plan.Entries[0].Task);
        Assert.AreEqual(0, plan.Entries[0].Position);
        Assert.AreEqual(second.Id, plan.Entries[1].Task.Id);
        Assert.AreEqual(1, plan.Entries[1].Position);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ReorderValidOrderChangesOnlyPositions()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask first = TestTaskFactory.Create(plannedStart: new TimeOnly(9, 0));
        DomainTask second = TestTaskFactory.Create(plannedStart: new TimeOnly(10, 0));
        DomainTask third = TestTaskFactory.Create(plannedStart: new TimeOnly(11, 0));
        await store.CreateTaskAsync(first, _testContext.CancellationToken);
        await store.CreateTaskAsync(second, _testContext.CancellationToken);
        await store.CreateTaskAsync(third, _testContext.CancellationToken);

        await store.ReorderTasksAsync(
            [third.Id, first.Id, second.Id],
            _testContext.CancellationToken);
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);

        Assert.AreEqual(third.Id, plan.Entries[0].Task.Id);
        Assert.AreEqual(new TimeOnly(11, 0), plan.Entries[0].Task.PlannedStart);
        Assert.AreEqual(first.Id, plan.Entries[1].Task.Id);
        Assert.AreEqual(new TimeOnly(9, 0), plan.Entries[1].Task.PlannedStart);
        Assert.AreEqual(second.Id, plan.Entries[2].Task.Id);
        Assert.AreEqual(new TimeOnly(10, 0), plan.Entries[2].Task.PlannedStart);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ReorderInvalidOrderRollsBackWithoutMutation()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask first = TestTaskFactory.Create();
        DomainTask second = TestTaskFactory.Create();
        await store.CreateTaskAsync(first, _testContext.CancellationToken);
        await store.CreateTaskAsync(second, _testContext.CancellationToken);

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await store.ReorderTasksAsync(
                [first.Id, first.Id],
                _testContext.CancellationToken));
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);

        Assert.Contains("duplicate", exception.Message);
        Assert.AreEqual(first.Id, plan.Entries[0].Task.Id);
        Assert.AreEqual(second.Id, plan.Entries[1].Task.Id);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task DeleteTaskRetainsRowAndCompactsPositions()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask first = TestTaskFactory.Create();
        DomainTask second = TestTaskFactory.Create();
        DomainTask third = TestTaskFactory.Create();
        await store.CreateTaskAsync(first, _testContext.CancellationToken);
        await store.CreateTaskAsync(second, _testContext.CancellationToken);
        await store.CreateTaskAsync(third, _testContext.CancellationToken);

        await store.DeleteTaskAsync(second.Id, _testContext.CancellationToken);
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);
        (long retainedRows, string? deletedAtUtc) = await ReadDeletedTaskAsync(
            database,
            second.Id,
            _testContext.CancellationToken);

        Assert.HasCount(2, plan.Entries);
        Assert.AreEqual(first.Id, plan.Entries[0].Task.Id);
        Assert.AreEqual(0, plan.Entries[0].Position);
        Assert.AreEqual(third.Id, plan.Entries[1].Task.Id);
        Assert.AreEqual(1, plan.Entries[1].Position);
        Assert.AreEqual(1L, retainedRows);
        Assert.IsNotNull(deletedAtUtc);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task PreCancelledCreateDoesNotMutateDatabase()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await store.CreateTaskAsync(TestTaskFactory.Create(), cancellation.Token));
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);

        Assert.IsEmpty(plan.Entries);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CorruptPersistedEnumThrowsInvalidDataFailure()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = TestTaskFactory.Create();
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        await CorruptTaskColumnAsync(
            database,
            task.Id,
            "timing_mode",
            "Unknown",
            _testContext.CancellationToken);

        InvalidDataException exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await store.LoadTodayPlanAsync(_testContext.CancellationToken));

        Assert.Contains(task.Id.ToString(), exception.Message);
        Assert.Contains(nameof(TimingMode), exception.Message);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CorruptPersistedTextThrowsInvalidDataFailure()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = TestTaskFactory.Create();
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        await CorruptTaskColumnAsync(
            database,
            task.Id,
            "full_title",
            " ",
            _testContext.CancellationToken);

        InvalidDataException exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await store.LoadTodayPlanAsync(_testContext.CancellationToken));

        Assert.Contains(task.Id.ToString(), exception.Message);
        Assert.Contains("invalid", exception.Message);
    }

    private static TodayPlanStore CreateStore(TestDatabase database)
    {
        return new TodayPlanStore(database.DatabasePath, new FixedTimeProvider(FixedNow));
    }

    private static void AssertTaskEqual(DomainTask expected, DomainTask actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.FullTitle, actual.FullTitle);
        Assert.AreEqual(expected.ShortFocusLabel, actual.ShortFocusLabel);
        Assert.AreEqual(expected.DefinitionOfDone, actual.DefinitionOfDone);
        Assert.AreEqual(expected.FirstPhysicalAction, actual.FirstPhysicalAction);
        Assert.AreEqual(expected.NextPhysicalAction, actual.NextPhysicalAction);
        Assert.AreEqual(expected.PlannedStart, actual.PlannedStart);
        Assert.AreEqual(expected.PlannedDuration, actual.PlannedDuration);
        Assert.AreEqual(expected.TimingMode, actual.TimingMode);
        Assert.AreEqual(expected.ScheduleType, actual.ScheduleType);
        Assert.AreEqual(expected.Importance, actual.Importance);
        Assert.AreEqual(expected.State, actual.State);
    }

    private static async System.Threading.Tasks.Task<(long Rows, string? DeletedAtUtc)>
        ReadDeletedTaskAsync(
            TestDatabase database,
            TaskId taskId,
            CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*), MAX(deleted_at_utc)
            FROM tasks
            WHERE task_id = $taskId;
            """;
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async System.Threading.Tasks.Task CorruptTaskColumnAsync(
        TestDatabase database,
        TaskId taskId,
        string columnName,
        string corruptValue,
        CancellationToken cancellationToken)
    {
        if (columnName is not ("timing_mode" or "full_title"))
        {
            throw new ArgumentOutOfRangeException(nameof(columnName));
        }

        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"UPDATE tasks SET {columnName} = $value WHERE task_id = $taskId;";
        command.Parameters.AddWithValue("$value", corruptValue);
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
