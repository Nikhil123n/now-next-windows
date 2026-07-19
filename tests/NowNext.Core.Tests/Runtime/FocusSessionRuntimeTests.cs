using System.Reflection;
using NowNext.App;
using NowNext.App.Persistence;
using NowNext.Core.Domain;
using NowNext.Core.Planning;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Runtime;

[TestClass]
public sealed class FocusSessionRuntimeTests
{
    private static readonly DateTimeOffset InitialUtc =
        new(2026, 7, 18, 13, 0, 0, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public FocusSessionRuntimeTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task RestartRestoresOnlyCommittedActiveTime()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create(
            plannedDuration: TimeSpan.FromMinutes(25),
            timingMode: TimingMode.CountUp);

        using (var firstStore = new TodayPlanStore(database.DatabasePath, clock))
        {
            await firstStore.CreateTaskAsync(task, _testContext.CancellationToken);
            using var firstRuntime = new FocusSessionRuntime(firstStore, clock);
            await firstRuntime.InitializeAsync(_testContext.CancellationToken);
            await firstRuntime.CreateAsync(
                new SessionId(Guid.NewGuid()),
                task.Id,
                task.TimingMode,
                task.PlannedDuration,
                _testContext.CancellationToken);
            await firstRuntime.ExecuteAsync(
                new StartSession(),
                _testContext.CancellationToken);

            clock.Advance(TimeSpan.FromMinutes(7));
            await firstRuntime.ExecuteAsync(
                new RefreshSession(),
                _testContext.CancellationToken);

            Assert.AreEqual(
                TimeSpan.FromMinutes(7),
                firstRuntime.Current!.CommittedActiveDuration);
        }

        clock.Advance(TimeSpan.FromMinutes(40));
        using var secondStore = new TodayPlanStore(database.DatabasePath, clock);
        using var secondRuntime = new FocusSessionRuntime(secondStore, clock);
        await secondRuntime.InitializeAsync(_testContext.CancellationToken);

        FocusSession restored = secondRuntime.Current!;
        _ = Assert.IsInstanceOfType<RecoveryRequiredSessionState>(restored.State);
        Assert.AreEqual(TimeSpan.FromMinutes(7), restored.CommittedActiveDuration);

        await secondRuntime.ExecuteAsync(
            new ResumeWithoutAwayTime(),
            _testContext.CancellationToken);

        FocusSession resumed = secondRuntime.Current!;
        _ = Assert.IsInstanceOfType<FocusingSessionState>(resumed.State);
        Assert.AreEqual(TimeSpan.FromMinutes(7), resumed.CommittedActiveDuration);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SuspensionCheckpointExcludesUnobservedAwayTime()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create(
            plannedDuration: TimeSpan.FromMinutes(30),
            timingMode: TimingMode.Countdown);

        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);
        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);

        clock.Advance(TimeSpan.FromMinutes(4));
        await runtime.InterruptForSuspensionAsync(_testContext.CancellationToken);
        clock.Advance(TimeSpan.FromHours(2));
        await runtime.ReloadAfterResumeAsync(_testContext.CancellationToken);

        FocusSession interrupted = runtime.Current!;
        _ = Assert.IsInstanceOfType<RecoveryRequiredSessionState>(interrupted.State);
        Assert.AreEqual(TimeSpan.FromMinutes(4), interrupted.CommittedActiveDuration);

        await runtime.ExecuteAsync(
            new ResumeWithoutAwayTime(),
            _testContext.CancellationToken);
        SessionView view = FocusSessionMachine.CreateView(runtime.Current!, clock);
        CountdownTimerReading reading =
            Assert.IsInstanceOfType<CountdownTimerReading>(view.Timer);
        Assert.AreEqual(TimeSpan.FromMinutes(26), reading.Remaining);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CancelledCommandDoesNotPublishOrPersistAChange()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(new StartSession(), cancellation.Token));

        _ = Assert.IsInstanceOfType<ReadySessionState>(runtime.Current!.State);
        SessionCheckpoint persisted = (await store.LoadCurrentSessionAsync(
            _testContext.CancellationToken))!;
        Assert.AreEqual(SessionCheckpointState.Ready, persisted.State);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task UnresolvedSessionCannotBeResetByCreate()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        SessionId originalId = new(Guid.NewGuid());
        await runtime.CreateAsync(
            originalId,
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => runtime.CreateAsync(
                    new SessionId(Guid.NewGuid()),
                    task.Id,
                    task.TimingMode,
                    task.PlannedDuration,
                    _testContext.CancellationToken));

        Assert.Contains("must be resolved", exception.Message);
        Assert.AreEqual(originalId, runtime.Current!.Id);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SuspensionUsesEventTimeWhileAnotherOperationOwnsGate()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);
        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(4));

        FieldInfo operationGateField = typeof(FocusSessionRuntime).GetField(
            "_operationGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        SemaphoreSlim operationGate =
            Assert.IsInstanceOfType<SemaphoreSlim>(operationGateField.GetValue(runtime));
        await operationGate.WaitAsync(_testContext.CancellationToken);
        try
        {
            System.Threading.Tasks.Task interruption = runtime.InterruptForSuspensionAsync(
                _testContext.CancellationToken);
            clock.Advance(TimeSpan.FromHours(2));
            operationGate.Release();
            await interruption;
        }
        finally
        {
            if (operationGate.CurrentCount == 0)
            {
                operationGate.Release();
            }
        }

        FocusSession interrupted = runtime.Current!;
        _ = Assert.IsInstanceOfType<RecoveryRequiredSessionState>(interrupted.State);
        Assert.AreEqual(TimeSpan.FromMinutes(4), interrupted.CommittedActiveDuration);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ParkPublishesOnlyAfterContextCapsuleIsCommitted()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);
        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);

        await runtime.ExecuteAsync(
            new ParkSession(
                "Open the next reviewer comment",
                "The first comment is already resolved."),
            _testContext.CancellationToken);
        ContextCapsule? capsule = await store.LoadLatestContextCapsuleAsync(
            task.Id,
            _testContext.CancellationToken);

        Assert.IsInstanceOfType<ParkedSessionState>(runtime.Current!.State);
        Assert.IsNotNull(capsule);
        Assert.AreEqual(runtime.Current.Id, capsule.SessionId);
        Assert.AreEqual("Open the next reviewer comment", capsule.NextPhysicalAction);
        Assert.AreEqual("The first comment is already resolved.", capsule.Note);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SubstantialAbsenceReloadPreservesLastDurableCheckpoint()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create(plannedDuration: TimeSpan.FromMinutes(30));
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);
        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(3));
        await runtime.ExecuteAsync(new RefreshSession(), _testContext.CancellationToken);

        clock.AdvanceMonotonic(TimeSpan.FromMinutes(15));
        clock.AdjustUtc(TimeSpan.FromHours(-8));
        await runtime.ReloadForSubstantialAbsenceAsync(_testContext.CancellationToken);

        RecoveryRequiredSessionState recovery =
            Assert.IsInstanceOfType<RecoveryRequiredSessionState>(runtime.Current!.State);
        Assert.AreEqual(ActiveSessionPhase.Focusing, recovery.InterruptedPhase);
        Assert.AreEqual(TimeSpan.FromMinutes(3), runtime.Current.CommittedActiveDuration);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CloseDayReleasesKeepAwakeOnlyAfterDurableClosure()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        await store.SaveDaySettingsAsync(
            new DaySettings(new DateOnly(2026, 7, 18), new TimeOnly(17, 0)),
            _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);
        ShutdownSummary summary = await store.CreateShutdownSummaryAsync(
            _testContext.CancellationToken);
        var controller = new ObservingKeepAwakeController(database.DatabasePath);

        await runtime.CloseDayAsync(summary, controller, _testContext.CancellationToken);

        Assert.IsTrue(controller.Released);
        Assert.IsTrue(controller.SawDurableClosure);
        Assert.IsInstanceOfType<DayClosedSessionState>(runtime.Current!.State);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task CloseDayPersistenceFailureDoesNotReleaseKeepAwake()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        await store.SaveDaySettingsAsync(
            new DaySettings(new DateOnly(2026, 7, 18), new TimeOnly(17, 0)),
            _testContext.CancellationToken);
        ShutdownSummary summary = await store.CreateShutdownSummaryAsync(
            _testContext.CancellationToken);
        await store.CloseDayAsync(summary, cancellationToken: _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        var controller = new ObservingKeepAwakeController(database.DatabasePath);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await runtime.CloseDayAsync(
                summary,
                controller,
                _testContext.CancellationToken));

        Assert.IsFalse(controller.Released);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task KeepAwakeReleaseFailureDoesNotReopenPersistedDay()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        await store.SaveDaySettingsAsync(
            new DaySettings(new DateOnly(2026, 7, 18), new TimeOnly(17, 0)),
            _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        ShutdownSummary summary = await store.CreateShutdownSummaryAsync(
            _testContext.CancellationToken);
        var controller = new ObservingKeepAwakeController(
            database.DatabasePath,
            throwOnRelease: true);

        DayClosure closure = await runtime.CloseDayAsync(
            summary,
            controller,
            _testContext.CancellationToken);

        Assert.IsTrue(controller.Released);
        Assert.AreEqual(summary.Date, closure.Date);
        Assert.IsNotNull(await store.LoadDayClosureAsync(_testContext.CancellationToken));
    }

    private sealed class ObservingKeepAwakeController : IKeepAwakeController
    {
        private readonly string _databasePath;
        private readonly bool _throwOnRelease;

        internal ObservingKeepAwakeController(
            string databasePath,
            bool throwOnRelease = false)
        {
            _databasePath = databasePath;
            _throwOnRelease = throwOnRelease;
        }

        internal bool Released { get; private set; }

        internal bool SawDurableClosure { get; private set; }

        public bool IsActive => false;

        public void Acquire()
        {
        }

        public void Release()
        {
            Released = true;
            if (_throwOnRelease)
            {
                throw new InvalidOperationException("Simulated keep-awake release failure.");
            }

            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Pooling = false,
                }.ToString());
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM day_closures;";
            SawDurableClosure = Convert.ToInt64(
                command.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
    }

}
