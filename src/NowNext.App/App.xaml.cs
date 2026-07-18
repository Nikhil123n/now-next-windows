using Microsoft.UI.Xaml;
using Microsoft.Windows.System.Power;
using NowNext.App.Persistence;

namespace NowNext.App;

public partial class App : Application, IDisposable
{
    private Window? _window;
    private TodayPlanStore? _store;
    private FocusSessionRuntime? _sessionRuntime;
    private bool _disposed;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        const string readyMessage = "NOW/NEXT prototype is ready.";
        const string storageFailureMessage = "NOW/NEXT could not initialize local storage.";
        string statusMessage;

        try
        {
            _store = TodayPlanStore.CreateForCurrentUser();
            await _store.InitializeAsync();
            _sessionRuntime = new FocusSessionRuntime(_store);
            await _sessionRuntime.InitializeAsync();
            PowerManager.SystemSuspendStatusChanged += OnSystemSuspendStatusChanged;
            statusMessage = readyMessage;
        }
        catch (Exception exception) when (
            exception is TodayPlanStorageException or InvalidDataException)
        {
            statusMessage = storageFailureMessage;
        }

        _window = new MainWindow(statusMessage);
        _window.Closed += OnWindowClosed;
        _window.Activate();
    }

    private async void OnSystemSuspendStatusChanged(object? sender, object args)
    {
        if (_sessionRuntime is null)
        {
            return;
        }

        try
        {
            switch (PowerManager.SystemSuspendStatus)
            {
                case SystemSuspendStatus.Entering:
                    await _sessionRuntime.InterruptForSuspensionAsync();
                    break;
                case SystemSuspendStatus.AutoResume:
                case SystemSuspendStatus.ManualResume:
                    await _sessionRuntime.ReloadAfterResumeAsync();
                    break;
            }
        }
        catch (Exception exception) when (
            exception is TodayPlanStorageException or InvalidDataException)
        {
            if (_window is MainWindow mainWindow)
            {
                _ = mainWindow.DispatcherQueue.TryEnqueue(
                    () => mainWindow.SetStatus(
                        "NOW/NEXT could not save local recovery state."));
            }
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        PowerManager.SystemSuspendStatusChanged -= OnSystemSuspendStatusChanged;
        _sessionRuntime?.Dispose();
        _store?.Dispose();
        _sessionRuntime = null;
        _store = null;
        _window = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
