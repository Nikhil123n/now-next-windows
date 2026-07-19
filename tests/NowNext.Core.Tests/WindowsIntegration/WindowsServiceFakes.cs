using NowNext.App;
using NowNext.App.Diagnostics;
using NowNext.App.WindowsIntegration;

namespace NowNext.Core.Tests.WindowsIntegration;

internal sealed class TestLocalState : IDisposable
{
    internal TestLocalState()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "NowNext.Core.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        Paths = new FakeApplicationDataPaths(
            RootPath,
            Path.Combine(RootPath, "now-next.db"));
    }

    internal string RootPath { get; }

    internal FakeApplicationDataPaths Paths { get; }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}

internal sealed class FakeApplicationDataPaths : IApplicationDataPaths
{
    internal FakeApplicationDataPaths(string localStatePath, string databasePath)
    {
        LocalStatePath = Path.GetFullPath(localStatePath);
        DatabasePath = Path.GetFullPath(databasePath);
        BackupDirectoryPath = Path.Combine(LocalStatePath, "Backups");
        ExportDirectoryPath = Path.Combine(LocalStatePath, "Exports");
        DiagnosticDirectoryPath = Path.Combine(LocalStatePath, "Diagnostics");
        DiagnosticLogPath = Path.Combine(DiagnosticDirectoryPath, "now-next.log.jsonl");
    }

    public string LocalStatePath { get; }

    public string DatabasePath { get; }

    public string BackupDirectoryPath { get; }

    public string ExportDirectoryPath { get; }

    public string DiagnosticDirectoryPath { get; }

    public string DiagnosticLogPath { get; }
}

internal sealed class FakeKeepAwakeController : IKeepAwakeController
{
    public bool IsActive { get; private set; }

    internal int AcquireCount { get; private set; }

    internal int ReleaseCount { get; private set; }

    public void Acquire()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        AcquireCount++;
    }

    public void Release()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        ReleaseCount++;
    }
}

internal sealed class FakeWindowsPowerEventSource : IWindowsPowerEventSource
{
    private bool _disposed;

    public event Func<WindowsPowerTransition, System.Threading.Tasks.Task>? Transitioned;

    internal async System.Threading.Tasks.Task RaiseAsync(WindowsPowerTransition transition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Transitioned is null)
        {
            return;
        }

        foreach (Delegate handler in Transitioned.GetInvocationList())
        {
            await ((Func<WindowsPowerTransition, System.Threading.Tasks.Task>)handler)(transition);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

internal sealed class FakeWindowsUserSettings : IWindowsUserSettings
{
    public bool KeepDisplayAwakeDuringSessions { get; set; }

    public bool StartFullScreen { get; set; }

    public void Clear()
    {
        KeepDisplayAwakeDuringSessions = false;
        StartFullScreen = false;
    }
}

internal sealed class FakeLaunchAtSignInService : ILaunchAtSignInService
{
    internal FakeLaunchAtSignInService(LaunchAtSignInState state = LaunchAtSignInState.Disabled)
    {
        State = state;
    }

    internal LaunchAtSignInState State { get; private set; }

    internal int ChangeCount { get; private set; }

    public System.Threading.Tasks.Task<LaunchAtSignInState> GetStateAsync()
    {
        return System.Threading.Tasks.Task.FromResult(State);
    }

    public System.Threading.Tasks.Task<LaunchAtSignInState> SetEnabledAsync(bool enabled)
    {
        ChangeCount++;
        State = enabled ? LaunchAtSignInState.Enabled : LaunchAtSignInState.Disabled;
        return System.Threading.Tasks.Task.FromResult(State);
    }
}

internal sealed class FakeReducedMotionPreference : IReducedMotionPreference
{
    internal FakeReducedMotionPreference(bool enabled)
    {
        IsReducedMotionEnabled = enabled;
    }

    public bool IsReducedMotionEnabled { get; }
}

internal sealed class FakeDiagnosticLog : ILocalDiagnosticLog
{
    internal List<(DiagnosticEventId EventId, DiagnosticResult Result, string? ExceptionType)>
        Entries
    { get; } = [];

    public System.Threading.Tasks.Task WriteAsync(
        DiagnosticEventId eventId,
        DiagnosticResult result,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entries.Add((eventId, result, exception?.GetType().FullName));
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
