namespace NowNext.Core.Domain;

public sealed class ScheduleEntry
{
    public ScheduleEntry(Task task, int position)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be zero or greater.");
        }

        Task = task;
        Position = position;
    }

    public Task Task { get; }

    public int Position { get; }
}
