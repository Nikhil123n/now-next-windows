using System.Globalization;

namespace NowNext.Core.Domain;

public readonly record struct TaskId
{
    public TaskId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Task ID must not be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString()
    {
        return Value.ToString("D", CultureInfo.InvariantCulture);
    }
}
