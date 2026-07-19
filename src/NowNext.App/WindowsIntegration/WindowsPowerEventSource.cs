using Microsoft.Windows.System.Power;

namespace NowNext.App.WindowsIntegration;

public enum WindowsPowerTransition
{
    Suspending,
    Resumed,
}

public interface IWindowsPowerEventSource : IDisposable
{
    public event Func<WindowsPowerTransition, System.Threading.Tasks.Task>? Transitioned;
}

public sealed class WindowsPowerEventSource : IWindowsPowerEventSource
{
    private bool _disposed;

    public WindowsPowerEventSource()
    {
        PowerManager.SystemSuspendStatusChanged += OnSystemSuspendStatusChanged;
    }

    public event Func<WindowsPowerTransition, System.Threading.Tasks.Task>? Transitioned;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        PowerManager.SystemSuspendStatusChanged -= OnSystemSuspendStatusChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async void OnSystemSuspendStatusChanged(object? sender, object args)
    {
        WindowsPowerTransition? transition = PowerManager.SystemSuspendStatus switch
        {
            SystemSuspendStatus.Entering => WindowsPowerTransition.Suspending,
            SystemSuspendStatus.AutoResume or SystemSuspendStatus.ManualResume =>
                WindowsPowerTransition.Resumed,
            _ => null,
        };
        if (transition is null || Transitioned is null)
        {
            return;
        }

        foreach (Delegate handler in Transitioned.GetInvocationList())
        {
            await ((Func<WindowsPowerTransition, System.Threading.Tasks.Task>)handler)(
                transition.Value);
        }
    }
}
