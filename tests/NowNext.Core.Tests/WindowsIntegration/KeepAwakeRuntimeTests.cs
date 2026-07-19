using NowNext.App;
using NowNext.App.Persistence;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.WindowsIntegration;

[TestClass]
public sealed class KeepAwakeRuntimeTests
{
    private readonly TestContext _testContext;

    public KeepAwakeRuntimeTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ActiveSessionAcquiresAndReleasesAtBoundaries()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 18, 13, 0, 0, TimeSpan.Zero));
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        var controller = new FakeKeepAwakeController();
        using var runtime = new FocusSessionRuntime(
            store,
            clock,
            controller,
            isKeepAwakeEnabled: () => true);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);

        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);
        Assert.IsTrue(controller.IsActive);
        Assert.AreEqual(1, controller.AcquireCount);

        await runtime.ExecuteAsync(new PauseSession(), _testContext.CancellationToken);
        Assert.IsFalse(controller.IsActive);
        Assert.AreEqual(1, controller.ReleaseCount);

        await runtime.ExecuteAsync(new ResumeSession(), _testContext.CancellationToken);
        Assert.IsTrue(controller.IsActive);
        Assert.AreEqual(2, controller.AcquireCount);

        await runtime.InterruptForSuspensionAsync(_testContext.CancellationToken);
        Assert.IsFalse(controller.IsActive);
        Assert.AreEqual(2, controller.ReleaseCount);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task DisabledPreferenceNeverAcquiresDisplayRequest()
    {
        using var database = new TestDatabase();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 18, 13, 0, 0, TimeSpan.Zero));
        DomainTask task = TestTaskFactory.Create();
        using var store = new TodayPlanStore(database.DatabasePath, clock);
        await store.CreateTaskAsync(task, _testContext.CancellationToken);
        var controller = new FakeKeepAwakeController();
        using var runtime = new FocusSessionRuntime(
            store,
            clock,
            controller,
            isKeepAwakeEnabled: () => false);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        await runtime.CreateAsync(
            new SessionId(Guid.NewGuid()),
            task.Id,
            task.TimingMode,
            task.PlannedDuration,
            _testContext.CancellationToken);

        await runtime.ExecuteAsync(new StartSession(), _testContext.CancellationToken);

        Assert.IsFalse(controller.IsActive);
        Assert.AreEqual(0, controller.AcquireCount);
    }
}
