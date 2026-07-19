using NowNext.App.Diagnostics;

namespace NowNext.Core.Tests.WindowsIntegration;

[TestClass]
public sealed class LocalDiagnosticLogTests
{
    private readonly TestContext _testContext;

    public LocalDiagnosticLogTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task WriteAsyncWithUserTextRecordsOnlyExceptionType()
    {
        using var localState = new TestLocalState();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 18, 13, 0, 0, TimeSpan.Zero));
        using var log = new LocalDiagnosticLog(localState.Paths, clock);
        const string sensitiveText = "Secret task title and capsule note";

        await log.WriteAsync(
            DiagnosticEventId.SuspendCheckpoint,
            DiagnosticResult.Failed,
            new InvalidOperationException(sensitiveText),
            _testContext.CancellationToken);

        string content = await File.ReadAllTextAsync(
            localState.Paths.DiagnosticLogPath,
            _testContext.CancellationToken);
        Assert.DoesNotContain(sensitiveText, content);
        Assert.Contains("SuspendCheckpoint", content);
        Assert.Contains(typeof(InvalidOperationException).FullName!, content);
        Assert.DoesNotContain("Message", content);
    }
}
