using System.Collections.ObjectModel;
using System.Globalization;
using NowNext.Core.Domain;

namespace NowNext.Core.Planning;

public readonly record struct ScheduleRepairId
{
    public ScheduleRepairId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Schedule repair ID must not be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString()
    {
        return Value.ToString("D", CultureInfo.InvariantCulture);
    }
}

public enum ScheduleRepairTriggerKind
{
    SessionExtended,
    CurrentTime,
    RecoveryRebuild,
}

public enum ScheduleRepairStatus
{
    NoChange,
    RequiresApproval,
    Impossible,
}

public enum ScheduleRepairIssue
{
    None,
    FixedCommitmentsOverlap,
    CurrentSessionOverlapsFixed,
    FixedCommitmentMissed,
    FixedCommitmentExceedsShutdown,
    ScheduleCrossesMidnight,
    ShutdownHasPassed,
    InsufficientFlexibleTime,
}

public sealed record ScheduleRepairTrigger
{
    public ScheduleRepairTrigger(
        ScheduleRepairTriggerKind kind,
        DateTimeOffset observedAtUtc,
        TimeSpan? extension = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Schedule repair trigger is not defined.", nameof(kind));
        }

        if (extension is not null && extension <= TimeSpan.Zero)
        {
            throw new ArgumentException("A recorded extension must be positive.", nameof(extension));
        }

        if (kind == ScheduleRepairTriggerKind.SessionExtended && extension is null)
        {
            throw new ArgumentException(
                "A session-extension trigger requires its extension duration.",
                nameof(extension));
        }

        if (kind != ScheduleRepairTriggerKind.SessionExtended && extension is not null)
        {
            throw new ArgumentException(
                "Only a session-extension trigger can contain an extension duration.",
                nameof(extension));
        }

        Kind = kind;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        Extension = extension;
    }

    public ScheduleRepairTriggerKind Kind { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public TimeSpan? Extension { get; }
}

public sealed record ScheduleRepairRequest
{
    public ScheduleRepairRequest(
        ScheduleRepairId id,
        long baseRevision,
        TodayPlan plan,
        TimeOnly currentTime,
        TimeOnly shutdownTime,
        ScheduleRepairTrigger trigger,
        TaskId? currentTaskId = null,
        TimeSpan? currentTaskRemaining = null)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("Schedule repair ID must not be empty.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(baseRevision);
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));

        if (currentTaskId is null && currentTaskRemaining is not null)
        {
            throw new ArgumentException(
                "Current-task remaining time requires a current task.",
                nameof(currentTaskRemaining));
        }

        if (currentTaskId is not null && currentTaskId.Value.Value == Guid.Empty)
        {
            throw new ArgumentException("Current task ID must not be empty.", nameof(currentTaskId));
        }

        if (currentTaskRemaining < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Current-task remaining time must not be negative.",
                nameof(currentTaskRemaining));
        }

        if (currentTaskId is not null
            && !plan.Entries.Any(entry => entry.Task.Id == currentTaskId.Value))
        {
            throw new ArgumentException(
                $"Current task ID '{currentTaskId}' is not in today's plan.",
                nameof(currentTaskId));
        }

        Id = id;
        BaseRevision = baseRevision;
        CurrentTime = currentTime;
        ShutdownTime = shutdownTime;
        CurrentTaskId = currentTaskId;
        CurrentTaskRemaining = currentTaskRemaining ?? TimeSpan.Zero;
    }

    public ScheduleRepairId Id { get; }

    public long BaseRevision { get; }

    public TodayPlan Plan { get; }

    public TimeOnly CurrentTime { get; }

    public TimeOnly ShutdownTime { get; }

    public ScheduleRepairTrigger Trigger { get; }

    public TaskId? CurrentTaskId { get; }

    public TimeSpan CurrentTaskRemaining { get; }
}

public sealed record BufferConsumption
{
    public BufferConsumption(TaskId? beforeTaskId, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Consumed buffer must be positive.", nameof(duration));
        }

        BeforeTaskId = beforeTaskId;
        Duration = duration;
    }

    public TaskId? BeforeTaskId { get; }

    public TimeSpan Duration { get; }
}

public sealed record ScheduleMove(
    TaskId TaskId,
    TimeOnly OriginalStart,
    TimeOnly RevisedStart,
    int OriginalPosition,
    int RevisedPosition);

public sealed record ScheduleDeferral(TaskId TaskId);

public sealed record ProtectedFixedCommitment(
    TaskId TaskId,
    TimeOnly Start,
    TimeSpan Duration);

public sealed class ScheduleRepairProposal
{
    internal ScheduleRepairProposal(
        ScheduleRepairRequest request,
        ScheduleRepairStatus status,
        ScheduleRepairIssue issue,
        IReadOnlyList<BufferConsumption> bufferConsumptions,
        IReadOnlyList<ScheduleMove> moves,
        ScheduleDeferral? deferral,
        TimeSpan revisedFinishFromMidnight,
        TimeSpan overflow,
        IReadOnlyList<ProtectedFixedCommitment> protectedFixedCommitments,
        IReadOnlyList<TaskId> revisedTaskOrder)
    {
        Request = request;
        Status = status;
        Issue = issue;
        BufferConsumptions = new ReadOnlyCollection<BufferConsumption>(
            bufferConsumptions.ToArray());
        Moves = new ReadOnlyCollection<ScheduleMove>(moves.ToArray());
        Deferral = deferral;
        RevisedFinishFromMidnight = revisedFinishFromMidnight;
        Overflow = overflow;
        ProtectedFixedCommitments = new ReadOnlyCollection<ProtectedFixedCommitment>(
            protectedFixedCommitments.ToArray());
        RevisedTaskOrder = new ReadOnlyCollection<TaskId>(revisedTaskOrder.ToArray());
    }

    public ScheduleRepairRequest Request { get; }

    public ScheduleRepairStatus Status { get; }

    public ScheduleRepairIssue Issue { get; }

    public IReadOnlyList<BufferConsumption> BufferConsumptions { get; }

    public TimeSpan BufferConsumed => TimeSpan.FromTicks(
        BufferConsumptions.Sum(item => item.Duration.Ticks));

    public IReadOnlyList<ScheduleMove> Moves { get; }

    public ScheduleDeferral? Deferral { get; }

    public TimeSpan RevisedFinishFromMidnight { get; }

    public TimeSpan Overflow { get; }

    public IReadOnlyList<ProtectedFixedCommitment> ProtectedFixedCommitments { get; }

    public IReadOnlyList<TaskId> RevisedTaskOrder { get; }

    public bool CanApply => Status == ScheduleRepairStatus.RequiresApproval;
}
