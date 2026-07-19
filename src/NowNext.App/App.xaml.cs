using Microsoft.UI.Xaml;
using NowNext.App.Diagnostics;
using NowNext.App.Persistence;
using NowNext.App.WindowsIntegration;

namespace NowNext.App;

public partial class App : Application, IDisposable
{
    private Window? _window;
    private TodayPlanStore? _store;
    private FocusSessionRuntime? _sessionRuntime;
    private IKeepAwakeController? _keepAwakeController;
    private WindowsLifecycleCoordinator? _lifecycleCoordinator;
    private DataMaintenanceService? _dataMaintenanceService;
    private LocalDiagnosticLog? _diagnosticLog;
    private bool _disposed;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        const string storageFailureMessage = "NOW/NEXT could not initialize local storage.";

        try
        {
            var paths = new WindowsApplicationDataPaths();
            var userSettings = new WindowsUserSettings();
            var reducedMotionPreference = new WindowsReducedMotionPreference();
            var launchAtSignInService = new WindowsLaunchAtSignInService();
            _diagnosticLog = new LocalDiagnosticLog(paths);
            _store = new TodayPlanStore(paths.DatabasePath);
            await _store.InitializeAsync();
            await TryWriteDiagnosticAsync(
                DiagnosticEventId.StorageInitialized,
                DiagnosticResult.Succeeded);
            _keepAwakeController = new WindowsDisplayKeepAwakeController();
            _sessionRuntime = new FocusSessionRuntime(
                _store,
                keepAwakeController: _keepAwakeController,
                isKeepAwakeEnabled: () => userSettings.KeepDisplayAwakeDuringSessions);
            await _sessionRuntime.InitializeAsync();
            _lifecycleCoordinator = new WindowsLifecycleCoordinator(
                _sessionRuntime,
                _keepAwakeController,
                new WindowsPowerEventSource(),
                _diagnosticLog);
            _dataMaintenanceService = new DataMaintenanceService(paths, _diagnosticLog);
            var mainWindow = new MainWindow(
                _store,
                _sessionRuntime,
                _keepAwakeController,
                _lifecycleCoordinator,
                _dataMaintenanceService,
                userSettings,
                launchAtSignInService,
                reducedMotionPreference);
            _lifecycleCoordinator.RecoveryReloaded += (_, _) =>
                _ = mainWindow.DispatcherQueue.TryEnqueue(
                    async () => await mainWindow.HandleRecoveryReloadedAsync());
            _lifecycleCoordinator.PersistenceFailed += (_, _) =>
                _ = mainWindow.DispatcherQueue.TryEnqueue(
                    () => mainWindow.SetStatus(
                        "NOW/NEXT could not save local recovery state."));
            _window = mainWindow;
            await TryWriteDiagnosticAsync(
                DiagnosticEventId.AppStarted,
                DiagnosticResult.Succeeded);
        }
        catch (Exception exception) when (
            exception is TodayPlanStorageException or InvalidDataException)
        {
            await TryWriteDiagnosticAsync(
                DiagnosticEventId.StorageInitialized,
                DiagnosticResult.Failed,
                exception);
            _window = new MainWindow(storageFailureMessage);
        }

        _window.Closed += OnWindowClosed;
        _window.Activate();
    }

    private async System.Threading.Tasks.Task TryWriteDiagnosticAsync(
        DiagnosticEventId eventId,
        DiagnosticResult result,
        Exception? exception = null)
    {
        if (_diagnosticLog is null)
        {
            return;
        }

        try
        {
            await _diagnosticLog.WriteAsync(eventId, result, exception);
        }
        catch (Exception diagnosticException) when (
            diagnosticException is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ObjectDisposedException)
        {
            // Startup must remain usable when the optional diagnostic file is unavailable.
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

        _lifecycleCoordinator?.Dispose();
        _sessionRuntime?.Dispose();
        _dataMaintenanceService?.Dispose();
        _store?.Dispose();
        _diagnosticLog?.Dispose();
        _lifecycleCoordinator = null;
        _dataMaintenanceService = null;
        _diagnosticLog = null;
        _sessionRuntime = null;
        _keepAwakeController = null;
        _store = null;
        _window = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
