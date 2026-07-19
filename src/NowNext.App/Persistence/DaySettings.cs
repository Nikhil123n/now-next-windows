using NowNext.Core.Domain;

namespace NowNext.App.Persistence;

public sealed record DaySettings
{
    public DaySettings(DateOnly date, TimeOnly shutdownTime, TaskId? dailyWinTaskId = null)
    {
        if (dailyWinTaskId is not null && dailyWinTaskId.Value.Value == Guid.Empty)
        {
            throw new ArgumentException("Daily Win task ID must not be empty.", nameof(dailyWinTaskId));
        }

        Date = date;
        ShutdownTime = shutdownTime;
        DailyWinTaskId = dailyWinTaskId;
    }

    public DateOnly Date { get; }

    public TimeOnly ShutdownTime { get; }

    public TaskId? DailyWinTaskId { get; }
}

public sealed record WorkdaySnapshot(
    NowNext.Core.Domain.TodayPlan Plan,
    long ScheduleRevision,
    DaySettings? Settings,
    DayClosure? Closure);
