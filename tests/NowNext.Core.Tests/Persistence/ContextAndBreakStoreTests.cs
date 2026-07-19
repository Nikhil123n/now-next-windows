using Microsoft.Data.Sqlite;
using NowNext.App.Persistence;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;
using NowNext.Core.Tests.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Persistence;

[TestClass]
public sealed class ContextAndBreakStoreTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 18, 13, 45, 30, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public ContextAndBreakStoreTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ParkCheckpointAndCapsuleRoundTripTogether()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = TestTaskFactory.Create();
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        SessionId sessionId = new(Guid.NewGuid());
        var checkpoint = new SessionCheckpoint(
            sessionId,
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            task.PlannedDuration,
            SessionCheckpointState.Parked,
            TimeSpan.FromMinutes(12),
            TimeSpan.Zero,
            TimeSpan.Zero,
            FixedNow,
            startedAtUtc: FixedNow.AddMinutes(-12),
            parkedAtUtc: FixedNow,
            parkedNextPhysicalAction: "Open the redlined draft");
        var capsule = new ContextCapsule(
            task.Id,
            sessionId,
            "Open the redlined draft",
            "Resolve the first reviewer question.",
            FixedNow);

        await store.SaveCurrentSessionAndContextAsync(
            checkpoint,
            capsule,
            _testContext.CancellationToken);
        SessionCheckpoint? restoredCheckpoint = await store.LoadCurrentSessionAsync(
            _testContext.CancellationToken);
        ContextCapsule? restoredCapsule = await store.LoadLatestContextCapsuleAsync(
            task.Id,
            _testContext.CancellationToken);
        TodayPlan plan = await store.LoadTodayPlanAsync(_testContext.CancellationToken);

        Assert.IsNotNull(restoredCheckpoint);
        Assert.AreEqual(SessionCheckpointState.Parked, restoredCheckpoint.State);
        Assert.AreEqual(capsule, restoredCapsule);
        Assert.AreEqual(TaskState.Parked, Assert.ContainsSingle(plan.Entries).Task.State);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CapsuleSurvivesTaskRemovalFromToday()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = TestTaskFactory.Create();
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        SessionId sessionId = new(Guid.NewGuid());
        var checkpoint = new SessionCheckpoint(
            sessionId,
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            task.PlannedDuration,
            SessionCheckpointState.Parked,
            TimeSpan.FromMinutes(4),
            TimeSpan.Zero,
            TimeSpan.Zero,
            FixedNow,
            parkedAtUtc: FixedNow,
            parkedNextPhysicalAction: "Reopen the outline");
        var capsule = new ContextCapsule(
            task.Id,
            sessionId,
            "Reopen the outline",
            null,
            FixedNow);
        await store.SaveCurrentSessionAndContextAsync(
            checkpoint,
            capsule,
            _testContext.CancellationToken);

        await store.DeleteTaskAsync(task.Id, _testContext.CancellationToken);
        ContextCapsule? restored = await store.LoadLatestContextCapsuleAsync(
            task.Id,
            _testContext.CancellationToken);

        Assert.AreEqual(capsule, restored);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task MismatchedCapsuleIsRejectedBeforeAnyMutation()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = TestTaskFactory.Create();
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        SessionId sessionId = new(Guid.NewGuid());
        var checkpoint = new SessionCheckpoint(
            sessionId,
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            task.PlannedDuration,
            SessionCheckpointState.Parked,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            FixedNow,
            parkedAtUtc: FixedNow,
            parkedNextPhysicalAction: "Open the draft");
        var mismatched = new ContextCapsule(
            task.Id,
            sessionId,
            "Open a different file",
            null,
            FixedNow);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await store.SaveCurrentSessionAndContextAsync(
                checkpoint,
                mismatched,
                _testContext.CancellationToken));
        Assert.IsNull(await store.LoadCurrentSessionAsync(_testContext.CancellationToken));
        Assert.IsNull(await store.LoadLatestContextCapsuleAsync(
            task.Id,
            _testContext.CancellationToken));
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task BreakSettingsDefaultAndCustomValuesRoundTrip()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);

        BreakSettings defaults = await store.LoadBreakSettingsAsync(
            _testContext.CancellationToken);
        var expected = new BreakSettings(
            TimeSpan.FromMinutes(8),
            "Slow ankle circles");
        await store.SaveBreakSettingsAsync(expected, _testContext.CancellationToken);
        BreakSettings actual = await store.LoadBreakSettingsAsync(
            _testContext.CancellationToken);

        Assert.AreEqual(TimeSpan.FromMinutes(5), defaults.DefaultBreakDuration);
        Assert.IsNull(defaults.UserSelectedMovement);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task RestartRestoresBreakLimitPromptAndCommittedTime()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = TestTaskFactory.Create();
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        var clock = new SessionTestClock();
        FocusSession session = FocusSessionMachine.Create(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration);
        session = FocusSessionMachine.Apply(session, new StartSession(), clock).Session;
        session = FocusSessionMachine.Apply(session, new CompleteSession(), clock).Session;
        session = FocusSessionMachine.Apply(
            session,
            new BeginBreak(new BreakPlan(
                TimeSpan.FromMinutes(7),
                new BreakPrompt(
                    BreakPromptKind.UserSelectedMovement,
                    "Slow ankle circles"))),
            clock).Session;
        clock.Advance(TimeSpan.FromMinutes(2));
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(session, clock);
        await store.SaveCurrentSessionAsync(checkpoint, _testContext.CancellationToken);

        SessionCheckpoint? restoredCheckpoint = await store.LoadCurrentSessionAsync(
            _testContext.CancellationToken);
        Assert.IsNotNull(restoredCheckpoint);
        FocusSession restored = FocusSessionMachine.Restore(restoredCheckpoint, clock);
        RecoveryRequiredSessionState recovery =
            Assert.IsInstanceOfType<RecoveryRequiredSessionState>(restored.State);

        Assert.AreEqual(TimeSpan.FromMinutes(2), restored.BreakDuration);
        Assert.AreEqual(TimeSpan.FromMinutes(7), recovery.BreakPlan?.Duration);
        Assert.AreEqual("Slow ankle circles", recovery.BreakPlan?.Prompt.Text);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CorruptCapsuleTextFailsClearly()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        DomainTask task = TestTaskFactory.Create();
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        SessionId sessionId = new(Guid.NewGuid());
        var checkpoint = new SessionCheckpoint(
            sessionId,
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            task.PlannedDuration,
            SessionCheckpointState.Parked,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            FixedNow,
            parkedAtUtc: FixedNow,
            parkedNextPhysicalAction: "Open the draft");
        await store.SaveCurrentSessionAndContextAsync(
            checkpoint,
            new ContextCapsule(
                task.Id,
                sessionId,
                "Open the draft",
                null,
                FixedNow),
            _testContext.CancellationToken);
        await using (SqliteConnection connection = database.CreateConnection())
        {
            await connection.OpenAsync(_testContext.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA ignore_check_constraints = ON;
                UPDATE context_capsules
                SET next_physical_action = ' '
                WHERE session_id = $sessionId;
                """;
            command.Parameters.AddWithValue("$sessionId", sessionId.ToString());
            await command.ExecuteNonQueryAsync(_testContext.CancellationToken);
        }

        InvalidDataException exception =
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                async () => await store.LoadLatestContextCapsuleAsync(
                    task.Id,
                    _testContext.CancellationToken));

        Assert.Contains("Context Capsule", exception.Message);
    }

    private static TodayPlanStore CreateStore(TestDatabase database)
    {
        return new TodayPlanStore(database.DatabasePath, new FixedTimeProvider(FixedNow));
    }
}
