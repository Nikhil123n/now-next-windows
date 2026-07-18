using NowNext.App.Persistence;
using NowNext.Core.Domain;
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
            await PersistBeforePublishingAsync(candidate, cancellationToken);
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
            await PersistBeforePublishingAsync(transition.Session, cancellationToken);
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
            await PersistBeforePublishingAsync(transition.Session, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        SessionCheckpoint checkpoint = FocusSessionMachine.CreateCheckpoint(
            candidate,
            _timeProvider);
        await _store.SaveCurrentSessionAsync(checkpoint, cancellationToken);
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
