using System.Collections.ObjectModel;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Domain;

public sealed class TodayPlan
{
    private readonly ReadOnlyCollection<ScheduleEntry> _entries;

    public TodayPlan(DateOnly date, IReadOnlyList<ScheduleEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        ScheduleEntry[] entryCopy = entries.ToArray();
        var taskIds = new HashSet<TaskId>();

        for (int index = 0; index < entryCopy.Length; index++)
        {
            ScheduleEntry entry = entryCopy[index]
                ?? throw new ArgumentException("Plan entries must not contain null.", nameof(entries));

            if (entry.Position != index)
            {
                throw new ArgumentException(
                    $"Plan positions must be contiguous from zero; expected position {index}.",
                    nameof(entries));
            }

            if (!taskIds.Add(entry.Task.Id))
            {
                throw new ArgumentException(
                    $"Plan contains duplicate task ID '{entry.Task.Id}'.",
                    nameof(entries));
            }
        }

        Date = date;
        _entries = Array.AsReadOnly(entryCopy);
    }

    public DateOnly Date { get; }

    public IReadOnlyList<ScheduleEntry> Entries => _entries;

    public TodayPlan Reorder(IReadOnlyList<TaskId> orderedTaskIds)
    {
        ArgumentNullException.ThrowIfNull(orderedTaskIds);

        if (orderedTaskIds.Count != _entries.Count)
        {
            throw new ArgumentException(
                "Reorder input must contain every current task exactly once.",
                nameof(orderedTaskIds));
        }

        Dictionary<TaskId, DomainTask> tasksById = _entries.ToDictionary(
            entry => entry.Task.Id,
            entry => entry.Task);
        var seenTaskIds = new HashSet<TaskId>();
        var reorderedEntries = new ScheduleEntry[orderedTaskIds.Count];

        for (int index = 0; index < orderedTaskIds.Count; index++)
        {
            TaskId taskId = orderedTaskIds[index];
            if (!seenTaskIds.Add(taskId))
            {
                throw new ArgumentException(
                    $"Reorder input contains duplicate task ID '{taskId}'.",
                    nameof(orderedTaskIds));
            }

            if (!tasksById.TryGetValue(taskId, out DomainTask? task))
            {
                throw new ArgumentException(
                    $"Reorder input contains unknown task ID '{taskId}'.",
                    nameof(orderedTaskIds));
            }

            reorderedEntries[index] = new ScheduleEntry(task, index);
        }

        return new TodayPlan(Date, reorderedEntries);
    }
}
