namespace NowNext.Core.Domain;

public sealed class Task
{
    public Task(
        TaskId id,
        string fullTitle,
        string shortFocusLabel,
        string definitionOfDone,
        string firstPhysicalAction,
        string? nextPhysicalAction,
        TimeOnly plannedStart,
        TimeSpan plannedDuration,
        TimingMode timingMode,
        ScheduleType scheduleType,
        TaskImportance importance,
        TaskState state)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("Task ID must not be empty.", nameof(id));
        }

        if (plannedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Planned duration must be positive.", nameof(plannedDuration));
        }

        ValidateEnum(timingMode, nameof(timingMode));
        ValidateEnum(scheduleType, nameof(scheduleType));
        ValidateEnum(importance, nameof(importance));
        ValidateEnum(state, nameof(state));

        Id = id;
        FullTitle = RequireText(fullTitle, nameof(fullTitle));
        ShortFocusLabel = RequireText(shortFocusLabel, nameof(shortFocusLabel));
        DefinitionOfDone = RequireText(definitionOfDone, nameof(definitionOfDone));
        FirstPhysicalAction = RequireText(firstPhysicalAction, nameof(firstPhysicalAction));
        NextPhysicalAction = OptionalText(nextPhysicalAction, nameof(nextPhysicalAction));
        PlannedStart = plannedStart;
        PlannedDuration = plannedDuration;
        TimingMode = timingMode;
        ScheduleType = scheduleType;
        Importance = importance;
        State = state;

        if (State == TaskState.Parked && NextPhysicalAction is null)
        {
            throw new ArgumentException(
                "A parked task must have a next physical action.",
                nameof(nextPhysicalAction));
        }
    }

    public TaskId Id { get; }

    public string FullTitle { get; }

    public string ShortFocusLabel { get; }

    public string DefinitionOfDone { get; }

    public string FirstPhysicalAction { get; }

    public string? NextPhysicalAction { get; }

    public TimeOnly PlannedStart { get; }

    public TimeSpan PlannedDuration { get; }

    public TimingMode TimingMode { get; }

    public ScheduleType ScheduleType { get; }

    public TaskImportance Importance { get; }

    public TaskState State { get; }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Required task text must not be blank.", parameterName);
        }

        return value.Trim();
    }

    private static string? OptionalText(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Optional task text must be null or non-blank.", parameterName);
        }

        return value.Trim();
    }

    private static void ValidateEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException($"{typeof(TEnum).Name} value is not defined.", parameterName);
        }
    }
}
