using NowNext.App.Persistence;
using NowNext.Core.Domain;
using NowNext.Core.Planning;
using NowNext.Core.Sessions;

namespace NowNext.App;

public sealed class FocusSessionRuntime : IDisposable
{
    private readonly TodayPlanStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private FocusSession? _current;
    private bool _disposed;

    public FocusSessionRuntime(TodayPlanStore store, TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public FocusSession? Current => Volatile.Read(ref _current);

    public SessionView GetCurrentView()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FocusSession current = Volatile.Read(ref _current)
            ?? throw new InvalidOperationException("There is no current focus session.");
        return FocusSessionMachine.CreateView(current, _timeProvider);
    }

    public async System.Threading.Tasks.Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            SessionCheckpoint? checkpoint = await _store.LoadCurrentSessionAsync(cancellationToken);
            Volatile.Write(ref _current, checkpoint is null
                ? null
                : FocusSessionMachine.Restore(checkpoint, _timeProvider));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async System.Threading.Tasks.Task<FocusSession> CreateAsync(
        SessionId sessionId,
        TaskId taskId,
        TimingMode timingMode,
        TimeSpan plannedDuration,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            FocusSession? current = Volatile.Read(ref _current);
            if (current is not null
                && current.State is not (
                    CompletedSessionState
                    or ParkedSessionState
                    or AbandonedSessionState
                    or DayClosedSessionState))
            {
                throw new InvalidOperationException(
                    $"Session '{current.Id}' must be resolved before another session is created.");
            }

            if (current?.Id == sessionId)
            {
                throw new InvalidOperationException(
                    $"Session ID '{sessionId}' has already been used by the current session.");
            }

            FocusSession candidate = FocusSessionMachine.Create(
                sessionId,
                taskId,
                timingMode,
                plannedDuration);
            await PersistBeforePublishingAsync(candidate, null, cancellationToken);
            Volatile.Write(ref _current, candidate);
            return candidate;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async System.Threading.Tasks.Task<SessionTransition> ExecuteAsync(
        SessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(command);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            FocusSession current = Volatile.Read(ref _current)
                ?? throw new InvalidOperationException("There is no current focus session.");
            SessionTransition transition = FocusSessionMachine.Apply(
                current,
                command,
                _timeProvider);
            await PersistBeforePublishingAsync(
                transition.Session,
                command as ParkSession,
                cancellationToken);
            Volatile.Write(ref _current, transition.Session);
            return transition;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async System.Threading.Tasks.Task InterruptForSuspensionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var interruptionObservation = new CapturedTimeProvider(
            _timeProvider.GetTimestamp(),
            _timeProvider.GetUtcNow(),
            _timeProvider.TimestampFrequency);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            FocusSession? current = Volatile.Read(ref _current);
            if (current?.State is not (
                FocusingSessionState
                or OvertimeSessionState
                or LandingSessionState
                or BreakSessionState))
            {
                return;
            }

            SessionTransition transition = FocusSessionMachine.Apply(
                current,
                new InterruptSession(),
                interruptionObservation);
            await PersistBeforePublishingAsync(transition.Session, null, cancellationToken);
            Volatile.Write(ref _current, transition.Session);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async System.Threading.Tasks.Task ReloadAfterResumeAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
    }

    public async System.Threading.Tasks.Task ReloadForSubstantialAbsenceAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            SessionCheckpoint? checkpoint = await _store.LoadCurrentSessionAsync(cancellationToken);
            if (checkpoint is null)
            {
                return;
            }

            Volatile.Write(
                ref _current,
                FocusSessionMachine.Restore(checkpoint, _timeProvider));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async System.Threading.Tasks.Task<DayClosure> CloseDayAsync(
        ShutdownSummary summary,
        IKeepAwakeController keepAwakeController,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(keepAwakeController);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            FocusSession? current = Volatile.Read(ref _current);
            FocusSession? closedSession = current;
            SessionCheckpoint? checkpoint = null;
            if (current?.State is not AbandonedSessionState)
            {
                if (current is not null)
                {
                    closedSession = FocusSessionMachine.Apply(
                        current,
                        new CloseDay(),
                        _timeProvider).Session;
                    checkpoint = FocusSessionMachine.CreateCheckpoint(
                        closedSession,
                        _timeProvider);
                }
            }

            DayClosure closure = await _store.CloseDayAsync(
                summary,
                checkpoint,
                cancellationToken);
            Volatile.Write(ref _current, closedSession);
            try
            {
                keepAwakeController.Release();
            }
            catch (InvalidOperationException)
            {
                // The persisted closure remains authoritative if Windows rejects release.
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The persisted closure remains authoritative if Windows rejects release.
            }

            return closure;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _operationGate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async System.Threading.Tasks.Task PersistBeforePublishingAsync(
        FocusSession candidate,
        ParkSession? parkSession,
        CancellationToken cancellationToken)
    {
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(
            candidate,
            _timeProvider);
        if (parkSession is null)
        {
            await _store.SaveCurrentSessionAsync(checkpoint, cancellationToken);
            return;
        }

        var capsule = new ContextCapsule(
            checkpoint.TaskId,
            checkpoint.Id,
            parkSession.NextPhysicalAction,
            parkSession.Note,
            checkpoint.ParkedAtUtc!.Value);
        await _store.SaveCurrentSessionAndContextAsync(
            checkpoint,
            capsule,
            cancellationToken);
    }

    private sealed class CapturedTimeProvider : TimeProvider
    {
        private readonly long _timestamp;
        private readonly DateTimeOffset _utcNow;

        internal CapturedTimeProvider(
            long timestamp,
            DateTimeOffset utcNow,
            long timestampFrequency)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
            _timestamp = timestamp;
            _utcNow = utcNow.ToUniversalTime();
            TimestampFrequency = timestampFrequency;
        }

        public override long TimestampFrequency { get; }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public override long GetTimestamp()
        {
            return _timestamp;
        }
    }
}
