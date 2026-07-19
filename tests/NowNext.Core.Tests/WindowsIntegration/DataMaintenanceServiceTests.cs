using NowNext.App;
using NowNext.App.Persistence;
using NowNext.App.WindowsIntegration;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.WindowsIntegration;

[TestClass]
public sealed class DataMaintenanceServiceTests
{
    private static readonly DateTimeOffset InitialUtc =
        new(2026, 7, 18, 13, 0, 0, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public DataMaintenanceServiceTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task BackupAndRestorePreserveExactDatabaseState()
    {
        using var localState = new TestLocalState();
        var clock = new ManualTimeProvider(InitialUtc);
        var diagnostics = new FakeDiagnosticLog();
        using var store = new TodayPlanStore(localState.Paths.DatabasePath, clock);
        DomainTask original = TestTaskFactory.Create(fullTitle: "Original task title");
        await store.CreateTaskAsync(original, _testContext.CancellationToken);
        using var maintenance = new DataMaintenanceService(
            localState.Paths,
            diagnostics,
            clock);

        string backupPath = await maintenance.CreateBackupAsync(
            _testContext.CancellationToken);
        DomainTask edited = TestTaskFactory.Create(
            id: original.Id,
            fullTitle: "Edited after backup",
            shortFocusLabel: original.ShortFocusLabel,
            definitionOfDone: original.DefinitionOfDone,
            firstPhysicalAction: original.FirstPhysicalAction,
            nextPhysicalAction: original.NextPhysicalAction,
            plannedStart: original.PlannedStart,
            plannedDuration: original.PlannedDuration,
            timingMode: original.TimingMode,
            scheduleType: original.ScheduleType,
            importance: original.Importance,
            state: original.State);
        await store.EditTaskAsync(edited, _testContext.CancellationToken);

        await maintenance.RestoreAsync(backupPath, _testContext.CancellationToken);

        TodayPlan restored = await store.LoadTodayPlanAsync(_testContext.CancellationToken);
        ScheduleEntry entry = Assert.ContainsSingle(restored.Entries);
        Assert.AreEqual(original.Id, entry.Task.Id);
        Assert.AreEqual("Original task title", entry.Task.FullTitle);
        Assert.IsTrue(File.Exists(backupPath));
        Assert.HasCount(2, diagnostics.Entries);
    }

    [TestMethod]
    [Timeout(15_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task BackupRestorePreservesCapsuleSettingsAndActiveRecovery()
    {
        using var localState = new TestLocalState();
        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask parkedTask = TestTaskFactory.Create(
            fullTitle: "Parked before backup",
            scheduleType: ScheduleType.Flexible);
        DomainTask activeTask = TestTaskFactory.Create(
            fullTitle: "Active before backup",
            timingMode: TimingMode.Countdown,
            scheduleType: ScheduleType.Flexible);
        using var store = new TodayPlanStore(localState.Paths.DatabasePath, clock);
        await store.CreateTaskAsync(parkedTask, _testContext.CancellationToken);
        await store.CreateTaskAsync(activeTask, _testContext.CancellationToken);
        using var runtime = new FocusSessionRuntime(store, clock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            parkedTask.Id,
            parkedTask.TimingMode,
            parkedTask.PlannedDuration,
            _testContext.CancellationToken);
        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(2));
        await runtime.ExecuteAsync(
            new ParkSession("Open the saved section", "Backup capsule"),
            _testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            activeTask.Id,
            activeTask.TimingMode,
            activeTask.PlannedDuration,
            _testContext.CancellationToken);
        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(4));
        await runtime.ExecuteAsync(new RefreshSession(), _testContext.CancellationToken);
        TodayPlan beforeBackup = await store.LoadTodayPlanAsync(
            _testContext.CancellationToken);
        await store.SaveDaySettingsAsync(
            new DaySettings(beforeBackup.Date, new TimeOnly(17, 0), activeTask.Id),
            _testContext.CancellationToken);
        using var maintenance = new DataMaintenanceService(
            localState.Paths,
            new FakeDiagnosticLog(),
            clock);
        string backupPath = await maintenance.CreateBackupAsync(
            _testContext.CancellationToken);

        await store.SaveDaySettingsAsync(
            new DaySettings(beforeBackup.Date, new TimeOnly(18, 0)),
            _testContext.CancellationToken);
        await maintenance.RestoreAsync(backupPath, _testContext.CancellationToken);
        clock.Advance(TimeSpan.FromHours(2));
        await runtime.ReloadAfterResumeAsync(_testContext.CancellationToken);
        WorkdaySnapshot restoredWorkday = await store.LoadWorkdaySnapshotAsync(
            _testContext.CancellationToken);
        ContextCapsule? restoredCapsule = await store.LoadLatestContextCapsuleAsync(
            parkedTask.Id,
            _testContext.CancellationToken);
        RecoveryRequiredSessionState recovery =
            Assert.IsInstanceOfType<RecoveryRequiredSessionState>(runtime.Current!.State);

        Assert.IsNotNull(restoredWorkday.Settings);
        Assert.AreEqual(new TimeOnly(17, 0), restoredWorkday.Settings.ShutdownTime);
        Assert.AreEqual(activeTask.Id, restoredWorkday.Settings.DailyWinTaskId);
        Assert.IsNotNull(restoredCapsule);
        Assert.AreEqual("Open the saved section", restoredCapsule.NextPhysicalAction);
        Assert.AreEqual(ActiveSessionPhase.Focusing, recovery.InterruptedPhase);
        Assert.AreEqual(TimeSpan.FromMinutes(4), runtime.Current.CommittedActiveDuration);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task RestoreRejectsCorruptSourceWithoutChangingLiveData()
    {
        using var localState = new TestLocalState();
        var clock = new ManualTimeProvider(InitialUtc);
        using var store = new TodayPlanStore(localState.Paths.DatabasePath, clock);
        DomainTask original = TestTaskFactory.Create(fullTitle: "Durable live task");
        await store.CreateTaskAsync(original, _testContext.CancellationToken);
        string corruptPath = Path.Combine(localState.RootPath, "corrupt.db");
        await File.WriteAllTextAsync(
            corruptPath,
            "not a sqlite database",
            _testContext.CancellationToken);
        using var maintenance = new DataMaintenanceService(
            localState.Paths,
            new FakeDiagnosticLog(),
            clock);

        await Assert.ThrowsExactlyAsync<DataMaintenanceException>(
            async () => await maintenance.RestoreAsync(
                corruptPath,
                _testContext.CancellationToken));

        TodayPlan live = await store.LoadTodayPlanAsync(_testContext.CancellationToken);
        Assert.AreEqual("Durable live task", Assert.ContainsSingle(live.Entries).Task.FullTitle);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ExportCreatesLoadableIndependentCopy()
    {
        using var localState = new TestLocalState();
        var clock = new ManualTimeProvider(InitialUtc);
        using var store = new TodayPlanStore(localState.Paths.DatabasePath, clock);
        DomainTask task = TestTaskFactory.Create();
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        using var maintenance = new DataMaintenanceService(
            localState.Paths,
            new FakeDiagnosticLog(),
            clock);
        string exportPath = Path.Combine(localState.RootPath, "selected-export.db");

        await maintenance.ExportAsync(exportPath, _testContext.CancellationToken);

        using var exportedStore = new TodayPlanStore(exportPath, clock);
        TodayPlan exported = await exportedStore.LoadTodayPlanAsync(
            _testContext.CancellationToken);
        Assert.AreEqual(task.Id, Assert.ContainsSingle(exported.Entries).Task.Id);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ResetClearsDatabaseSettingsAndLocalCopies()
    {
        using var localState = new TestLocalState();
        var clock = new ManualTimeProvider(InitialUtc);
        using var store = new TodayPlanStore(localState.Paths.DatabasePath, clock);
        await store.CreateTaskAsync(
            TestTaskFactory.Create(),
            _testContext.CancellationToken);
        var settings = new FakeWindowsUserSettings
        {
            KeepDisplayAwakeDuringSessions = true,
            StartFullScreen = true,
        };
        var launch = new FakeLaunchAtSignInService(LaunchAtSignInState.Enabled);
        using var maintenance = new DataMaintenanceService(
            localState.Paths,
            new FakeDiagnosticLog(),
            clock);
        _ = await maintenance.CreateBackupAsync(_testContext.CancellationToken);
        Directory.CreateDirectory(localState.Paths.ExportDirectoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(localState.Paths.ExportDirectoryPath, "export.db"),
            "old export",
            _testContext.CancellationToken);
        Directory.CreateDirectory(localState.Paths.DiagnosticDirectoryPath);
        await File.WriteAllTextAsync(
            localState.Paths.DiagnosticLogPath,
            "old diagnostic",
            _testContext.CancellationToken);

        await maintenance.ResetAsync(
            settings,
            launch,
            _testContext.CancellationToken);

        TodayPlan reset = await store.LoadTodayPlanAsync(_testContext.CancellationToken);
        Assert.IsEmpty(reset.Entries);
        Assert.IsFalse(settings.KeepDisplayAwakeDuringSessions);
        Assert.IsFalse(settings.StartFullScreen);
        Assert.AreEqual(LaunchAtSignInState.Disabled, launch.State);
        Assert.IsFalse(Directory.Exists(localState.Paths.BackupDirectoryPath));
        Assert.IsFalse(Directory.Exists(localState.Paths.ExportDirectoryPath));
        Assert.IsFalse(Directory.Exists(localState.Paths.DiagnosticDirectoryPath));
    }

    [TestMethod]
    public async System.Threading.Tasks.Task PreCancelledBackupDoesNotCreateBackup()
    {
        using var localState = new TestLocalState();
        var clock = new ManualTimeProvider(InitialUtc);
        using var store = new TodayPlanStore(localState.Paths.DatabasePath, clock);
        await store.InitializeAsync(_testContext.CancellationToken);
        using var maintenance = new DataMaintenanceService(
            localState.Paths,
            new FakeDiagnosticLog(),
            clock);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await maintenance.CreateBackupAsync(cancellationSource.Token));

        Assert.IsFalse(Directory.Exists(localState.Paths.BackupDirectoryPath));
    }
}
