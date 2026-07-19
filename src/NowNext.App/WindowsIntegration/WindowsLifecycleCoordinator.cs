using NowNext.App.Diagnostics;

namespace NowNext.App.WindowsIntegration;

public sealed class WindowsLifecycleCoordinator : IDisposable
{
    private readonly FocusSessionRuntime _sessionRuntime;
    private readonly IKeepAwakeController _keepAwakeController;
    private readonly IWindowsPowerEventSource _powerEvents;
    private readonly ILocalDiagnosticLog _diagnosticLog;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public WindowsLifecycleCoordinator(
        FocusSessionRuntime sessionRuntime,
        IKeepAwakeController keepAwakeController,
        IWindowsPowerEventSource powerEvents,
        ILocalDiagnosticLog diagnosticLog)
    {
        _sessionRuntime = sessionRuntime ?? throw new ArgumentNullException(nameof(sessionRuntime));
        _keepAwakeController = keepAwakeController
            ?? throw new ArgumentNullException(nameof(keepAwakeController));
        _powerEvents = powerEvents ?? throw new ArgumentNullException(nameof(powerEvents));
        _diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
        _powerEvents.Transitioned += OnPowerTransitionAsync;
    }

    public event EventHandler? RecoveryReloaded;

    public event EventHandler? PersistenceFailed;

    public System.Threading.Tasks.Task PersistBeforeExitAsync(
        CancellationToken cancellationToken = default)
    {
        return PersistInterruptionAsync(
            DiagnosticEventId.AppStopped,
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _powerEvents.Transitioned -= OnPowerTransitionAsync;
        _powerEvents.Dispose();
        TryReleaseKeepAwake();
        _operationGate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async System.Threading.Tasks.Task OnPowerTransitionAsync(
        WindowsPowerTransition transition)
    {
        if (_disposed)
        {
            return;
        }

        if (transition == WindowsPowerTransition.Suspending)
        {
            await PersistInterruptionAsync(DiagnosticEventId.SuspendCheckpoint);
            return;
        }

        await ReloadRecoveryAsync();
    }

    private async System.Threading.Tasks.Task PersistInterruptionAsync(
        DiagnosticEventId diagnosticEvent,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await _sessionRuntime.InterruptForSuspensionAsync(cancellationToken);
                await TryWriteDiagnosticAsync(
                    diagnosticEvent,
                    DiagnosticResult.Succeeded,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                await TryWriteDiagnosticAsync(
                    diagnosticEvent,
                    DiagnosticResult.Failed,
                    exception,
                    cancellationToken);
                PersistenceFailed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                TryReleaseKeepAwake();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async System.Threading.Tasks.Task ReloadRecoveryAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            TryReleaseKeepAwake();
            try
            {
                await _sessionRuntime.ReloadAfterResumeAsync();
                await TryWriteDiagnosticAsync(
                    DiagnosticEventId.ResumeRecovery,
                    DiagnosticResult.Succeeded);
                RecoveryReloaded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                await TryWriteDiagnosticAsync(
                    DiagnosticEventId.ResumeRecovery,
                    DiagnosticResult.Failed,
                    exception);
                PersistenceFailed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async System.Threading.Tasks.Task TryWriteDiagnosticAsync(
        DiagnosticEventId eventId,
        DiagnosticResult result,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _diagnosticLog.WriteAsync(
                eventId,
                result,
                exception,
                cancellationToken);
        }
        catch (Exception diagnosticException) when (
            diagnosticException is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ObjectDisposedException)
        {
            // Diagnostics are best effort and never weaken recovery persistence.
        }
    }

    private void TryReleaseKeepAwake()
    {
        try
        {
            _keepAwakeController.Release();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.Runtime.InteropServices.ExternalException
                or ObjectDisposedException)
        {
            // A failed Windows release cannot change durable session state.
        }
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is
            InvalidOperationException
            or InvalidDataException
            or Persistence.TodayPlanStorageException
            or OperationCanceledException;
    }
}
