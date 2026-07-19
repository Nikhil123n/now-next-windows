using System.Collections.ObjectModel;
using NowNext.Core.Domain;

namespace NowNext.Core.Planning;

public sealed record SessionActual
{
    public SessionActual(TaskId taskId, TimeSpan activeDuration)
    {
        if (taskId.Value == Guid.Empty)
        {
            throw new ArgumentException("Session actual task ID must not be empty.", nameof(taskId));
        }

        if (activeDuration < TimeSpan.Zero)
        {
            throw new ArgumentException("Session actual duration must not be negative.", nameof(activeDuration));
        }

        TaskId = taskId;
        ActiveDuration = activeDuration;
    }

    public TaskId TaskId { get; }

    public TimeSpan ActiveDuration { get; }
}

public sealed record RecoveryOverview(
    TaskId InterruptedTaskId,
    TaskId? NextFixedTaskId,
    TimeOnly? NextFixedStart,
    TimeSpan RealisticAvailableTime);

public enum DailyWinStatus
{
    NotSelected,
    Completed,
    NotCompleted,
}

public sealed record ShutdownTaskSummary(
    TaskId TaskId,
    TimeSpan PlannedDuration,
    TimeSpan ActualDuration);

public sealed class ShutdownSummary
{
    internal ShutdownSummary(
        DateOnly date,
        IReadOnlyList<ShutdownTaskSummary> completed,
        IReadOnlyList<ShutdownTaskSummary> deferred,
        TimeSpan totalPlannedDuration,
        TimeSpan totalActualDuration,
        TaskId? dailyWinTaskId,
        DailyWinStatus dailyWinStatus,
        TaskId? nextUnfinishedTaskId,
        string? nextPhysicalAction)
    {
        Date = date;
        Completed = new ReadOnlyCollection<ShutdownTaskSummary>(completed.ToArray());
        Deferred = new ReadOnlyCollection<ShutdownTaskSummary>(deferred.ToArray());
        TotalPlannedDuration = totalPlannedDuration;
        TotalActualDuration = totalActualDuration;
        DailyWinTaskId = dailyWinTaskId;
        DailyWinStatus = dailyWinStatus;
        NextUnfinishedTaskId = nextUnfinishedTaskId;
        NextPhysicalAction = nextPhysicalAction;
    }

    public DateOnly Date { get; }

    public IReadOnlyList<ShutdownTaskSummary> Completed { get; }

    public IReadOnlyList<ShutdownTaskSummary> Deferred { get; }

    public TimeSpan TotalPlannedDuration { get; }

    public TimeSpan TotalActualDuration { get; }

    public TaskId? DailyWinTaskId { get; }

    public DailyWinStatus DailyWinStatus { get; }

    public TaskId? NextUnfinishedTaskId { get; }

    public string? NextPhysicalAction { get; }
}

public static class WorkdayProjections
{
    public static RecoveryOverview CreateRecoveryOverview(
        TodayPlan plan,
        TaskId interruptedTaskId,
        TimeOnly currentTime,
        TimeOnly shutdownTime)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.Entries.Any(entry => entry.Task.Id == interruptedTaskId))
        {
            throw new ArgumentException(
                $"Interrupted task ID '{interruptedTaskId}' is not in today's plan.",
                nameof(interruptedTaskId));
        }

        TimeSpan now = currentTime.ToTimeSpan();
        TimeSpan shutdown = shutdownTime.ToTimeSpan();
        ScheduleEntry? nextFixed = plan.Entries
            .Where(entry => entry.Task.Id != interruptedTaskId)
            .Where(entry => entry.Task.ScheduleType == ScheduleType.Fixed)
            .Where(entry => entry.Task.State is not (TaskState.Completed or TaskState.Deferred))
            .OrderBy(entry => entry.Task.PlannedStart)
            .ThenBy(entry => entry.Position)
            .FirstOrDefault(entry =>
                entry.Task.PlannedStart.ToTimeSpan() + entry.Task.PlannedDuration > now);
        TimeSpan availableUntil = nextFixed is null
            ? shutdown
            : nextFixed.Task.PlannedStart.ToTimeSpan() < shutdown
                ? nextFixed.Task.PlannedStart.ToTimeSpan()
                : shutdown;
        TimeSpan available = availableUntil > now ? availableUntil - now : TimeSpan.Zero;
        return new RecoveryOverview(
            interruptedTaskId,
            nextFixed?.Task.Id,
            nextFixed?.Task.PlannedStart,
            available);
    }

    public static ShutdownSummary CreateShutdownSummary(
        TodayPlan plan,
        IReadOnlyList<SessionActual> sessionActuals,
        TaskId? dailyWinTaskId,
        IReadOnlyDictionary<TaskId, string>? latestNextActions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sessionActuals);
        latestNextActions ??= new Dictionary<TaskId, string>();

        if (dailyWinTaskId is not null
            && !plan.Entries.Any(entry => entry.Task.Id == dailyWinTaskId.Value))
        {
            throw new ArgumentException(
                $"Daily Win task ID '{dailyWinTaskId}' is not in today's plan.",
                nameof(dailyWinTaskId));
        }

        Dictionary<TaskId, TimeSpan> actualByTask = sessionActuals
            .GroupBy(actual => actual.TaskId)
            .ToDictionary(
                group => group.Key,
                group => TimeSpan.FromTicks(group.Sum(actual => actual.ActiveDuration.Ticks)));
        ShutdownTaskSummary[] completed = CreateTaskSummaries(
            plan,
            actualByTask,
            TaskState.Completed);
        ShutdownTaskSummary[] deferred = CreateTaskSummaries(
            plan,
            actualByTask,
            TaskState.Deferred);
        ScheduleEntry? next = plan.Entries
            .Where(entry => entry.Task.State is not (TaskState.Completed or TaskState.Deferred))
            .OrderBy(entry => entry.Task.Importance == TaskImportance.Important ? 0 : 1)
            .ThenBy(entry => entry.Position)
            .FirstOrDefault();
        string? nextAction = null;
        if (next is not null)
        {
            nextAction = latestNextActions.TryGetValue(next.Task.Id, out string? stored)
                && !string.IsNullOrWhiteSpace(stored)
                    ? stored.Trim()
                    : next.Task.NextPhysicalAction ?? next.Task.FirstPhysicalAction;
        }

        DailyWinStatus dailyWinStatus = dailyWinTaskId is null
            ? DailyWinStatus.NotSelected
            : plan.Entries.Single(entry => entry.Task.Id == dailyWinTaskId.Value).Task.State
                == TaskState.Completed
                    ? DailyWinStatus.Completed
                    : DailyWinStatus.NotCompleted;
        return new ShutdownSummary(
            plan.Date,
            completed,
            deferred,
            TimeSpan.FromTicks(plan.Entries.Sum(entry => entry.Task.PlannedDuration.Ticks)),
            TimeSpan.FromTicks(actualByTask.Values.Sum(duration => duration.Ticks)),
            dailyWinTaskId,
            dailyWinStatus,
            next?.Task.Id,
            nextAction);
    }

    private static ShutdownTaskSummary[] CreateTaskSummaries(
        TodayPlan plan,
        IReadOnlyDictionary<TaskId, TimeSpan> actualByTask,
        TaskState state)
    {
        return plan.Entries
            .Where(entry => entry.Task.State == state)
            .Select(entry => new ShutdownTaskSummary(
                entry.Task.Id,
                entry.Task.PlannedDuration,
                actualByTask.GetValueOrDefault(entry.Task.Id)))
            .ToArray();
    }
}
