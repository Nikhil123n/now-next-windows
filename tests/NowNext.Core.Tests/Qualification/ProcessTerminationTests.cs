using System.Diagnostics;
using NowNext.App;
using NowNext.App.Persistence;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Qualification;

[TestClass]
public sealed class ProcessTerminationTests
{
    private const string ChildDatabaseVariable = "NOWNEXT_QUALIFICATION_CHILD_DATABASE";
    private const string ChildReadyVariable = "NOWNEXT_QUALIFICATION_CHILD_READY";
    private static readonly DateTimeOffset InitialUtc =
        new(2026, 7, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public ProcessTerminationTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ForcedProcessTerminationRetainsCommittedCheckpoint()
    {
        using var database = new TestDatabase();
        string readyPath = Path.Combine(
            Path.GetTempPath(),
            $"now-next-forced-termination-{Guid.NewGuid():N}.ready");
        string executablePath = Path.ChangeExtension(
            typeof(ProcessTerminationTests).Assembly.Location,
            ".exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The Microsoft.Testing.Platform executable was not built.",
                executablePath);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add(
            "FullyQualifiedName=NowNext.Core.Tests.Qualification.ProcessTerminationTests.ForcedTerminationChildCommitsCheckpoint");
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add("off");
        startInfo.Environment[ChildDatabaseVariable] = database.DatabasePath;
        startInfo.Environment[ChildReadyVariable] = readyPath;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The forced-termination child did not start.");
        System.Threading.Tasks.Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
            _testContext.CancellationToken);
        System.Threading.Tasks.Task<string> standardError = process.StandardError.ReadToEndAsync(
            _testContext.CancellationToken);

        await process.WaitForExitAsync(_testContext.CancellationToken);
        string output = await standardOutput;
        string error = await standardError;
        Assert.AreNotEqual(
            0,
            process.ExitCode,
            $"The child should have been forcibly terminated. Output: {output} Error: {error}");
        Assert.IsTrue(
            File.Exists(readyPath),
            $"The child did not commit its checkpoint before termination. Output: {output} Error: {error}");

        var restartedClock = new ManualTimeProvider(InitialUtc.AddHours(2), timestamp: 0);
        using var store = new TodayPlanStore(database.DatabasePath, restartedClock);
        using var runtime = new FocusSessionRuntime(store, restartedClock);
        await runtime.InitializeAsync(_testContext.CancellationToken);
        FocusSession restored = runtime.Current!;
        RecoveryRequiredSessionState recovery =
            Assert.IsInstanceOfType<RecoveryRequiredSessionState>(restored.State);

        Assert.AreEqual(ActiveSessionPhase.Focusing, recovery.InterruptedPhase);
        Assert.AreEqual(TimeSpan.FromMinutes(7), restored.CommittedActiveDuration);
        File.Delete(readyPath);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task ForcedTerminationChildCommitsCheckpoint()
    {
        string? databasePath = Environment.GetEnvironmentVariable(ChildDatabaseVariable);
        string? readyPath = Environment.GetEnvironmentVariable(ChildReadyVariable);
        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(readyPath))
        {
            return;
        }

        var clock = new ManualTimeProvider(InitialUtc);
        DomainTask task = TestTaskFactory.Create(
            plannedDuration: TimeSpan.FromMinutes(25),
            timingMode: TimingMode.CountUp,
            scheduleType: ScheduleType.Flexible);
        using var store = new TodayPlanStore(databasePath, clock);
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
        clock.Advance(TimeSpan.FromMinutes(7));
        await runtime.ExecuteAsync(new RefreshSession(), _testContext.CancellationToken);
        await File.WriteAllTextAsync(
            readyPath,
            "checkpoint committed",
            _testContext.CancellationToken);

        Process.GetCurrentProcess().Kill();
        Assert.Fail("The forced-termination child continued after Process.Kill.");
    }
}
