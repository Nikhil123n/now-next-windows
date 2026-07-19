using NowNext.App;
using NowNext.App.Diagnostics;
using NowNext.App.Persistence;
using NowNext.App.WindowsIntegration;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.WindowsIntegration;

[TestClass]
public sealed class WindowsLifecycleCoordinatorTests
{
    private readonly TestContext _testContext;

    public WindowsLifecycleCoordinatorTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task SuspendThenResumeRestoresCommittedTimeInRecovery()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 18, 13, 0, 0, TimeSpan.Zero));
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        var keepAwake = new FakeKeepAwakeController();
        using var runtime = new FocusSessionRuntime(
            store,
            clock,
            keepAwake,
            isKeepAwakeEnabled: () => true);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);
        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(4));
        var powerEvents = new FakeWindowsPowerEventSource();
        var diagnostics = new FakeDiagnosticLog();
        using var coordinator = new WindowsLifecycleCoordinator(
            runtime,
            keepAwake,
            powerEvents,
            diagnostics);
        int recoveryNotifications = 0;
        coordinator.RecoveryReloaded += (_, _) => recoveryNotifications++;

        await powerEvents.RaiseAsync(WindowsPowerTransition.Suspending);
        clock.Advance(TimeSpan.FromHours(3));
        await powerEvents.RaiseAsync(WindowsPowerTransition.Resumed);

        FocusSession recovered = runtime.Current!;
        _ = Assert.IsInstanceOfType<RecoveryRequiredSessionState>(recovered.State);
        Assert.AreEqual(TimeSpan.FromMinutes(4), recovered.CommittedActiveDuration);
        Assert.IsFalse(keepAwake.IsActive);
        Assert.AreEqual(1, recoveryNotifications);
        Assert.HasCount(2, diagnostics.Entries);
        Assert.AreEqual(DiagnosticEventId.SuspendCheckpoint, diagnostics.Entries[0].EventId);
        Assert.AreEqual(DiagnosticResult.Succeeded, diagnostics.Entries[0].Result);
        Assert.AreEqual(DiagnosticEventId.ResumeRecovery, diagnostics.Entries[1].EventId);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ExitCheckpointInterruptsBeforeReportingSuccess()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 18, 13, 0, 0, TimeSpan.Zero));
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        var keepAwake = new FakeKeepAwakeController();
        using var runtime = new FocusSessionRuntime(
            store,
            clock,
            keepAwake,
            isKeepAwakeEnabled: () => true);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);
        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(6));
        var diagnostics = new FakeDiagnosticLog();
        using var coordinator = new WindowsLifecycleCoordinator(
            runtime,
            keepAwake,
            new FakeWindowsPowerEventSource(),
            diagnostics);

        await coordinator.PersistBeforeExitAsync(_testContext.CancellationToken);

        _ = Assert.IsInstanceOfType<RecoveryRequiredSessionState>(runtime.Current!.State);
        Assert.AreEqual(TimeSpan.FromMinutes(6), runtime.Current.CommittedActiveDuration);
        Assert.IsFalse(keepAwake.IsActive);
        Assert.ContainsSingle(diagnostics.Entries);
        Assert.AreEqual(DiagnosticEventId.AppStopped, diagnostics.Entries[0].EventId);
    }
}
