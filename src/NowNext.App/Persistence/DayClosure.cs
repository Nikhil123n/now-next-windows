using System.Collections.ObjectModel;
using NowNext.Core.Domain;
using NowNext.Core.Planning;

namespace NowNext.App.Persistence;

public sealed record DayClosureItem(
    TaskId TaskId,
    TaskState Outcome,
    TimeSpan PlannedDuration,
    TimeSpan ActualDuration);

public sealed class DayClosure
{
    public DayClosure(
        DateOnly date,
        DateTimeOffset closedAtUtc,
        TimeSpan totalPlannedDuration,
        TimeSpan totalActualDuration,
        TaskId? dailyWinTaskId,
        DailyWinStatus dailyWinStatus,
        TaskId? nextUnfinishedTaskId,
        string? nextPhysicalAction,
        IReadOnlyList<DayClosureItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (totalPlannedDuration < TimeSpan.Zero || totalActualDuration < TimeSpan.Zero)
        {
            throw new ArgumentException("Day closure durations must not be negative.");
        }

        if (!Enum.IsDefined(dailyWinStatus))
        {
            throw new ArgumentException("Daily Win status is not defined.", nameof(dailyWinStatus));
        }

        if (nextUnfinishedTaskId is null && nextPhysicalAction is not null
            || nextUnfinishedTaskId is not null && string.IsNullOrWhiteSpace(nextPhysicalAction))
        {
            throw new ArgumentException(
                "A next unfinished task and its physical action must appear together.",
                nameof(nextPhysicalAction));
        }

        Date = date;
        ClosedAtUtc = closedAtUtc.ToUniversalTime();
        TotalPlannedDuration = totalPlannedDuration;
        TotalActualDuration = totalActualDuration;
        DailyWinTaskId = dailyWinTaskId;
        DailyWinStatus = dailyWinStatus;
        NextUnfinishedTaskId = nextUnfinishedTaskId;
        NextPhysicalAction = nextPhysicalAction?.Trim();
        Items = new ReadOnlyCollection<DayClosureItem>(items.ToArray());
    }

    public DateOnly Date { get; }

    public DateTimeOffset ClosedAtUtc { get; }

    public TimeSpan TotalPlannedDuration { get; }

    public TimeSpan TotalActualDuration { get; }

    public TaskId? DailyWinTaskId { get; }

    public DailyWinStatus DailyWinStatus { get; }

    public TaskId? NextUnfinishedTaskId { get; }

    public string? NextPhysicalAction { get; }

    public IReadOnlyList<DayClosureItem> Items { get; }
}
