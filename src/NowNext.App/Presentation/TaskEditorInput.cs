using NowNext.Core.Domain;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.App.Presentation;

public sealed record TaskEditorInput(
    string FullTitle,
    string ShortFocusLabel,
    string DefinitionOfDone,
    string FirstPhysicalAction,
    TimeSpan? PlannedStart,
    double PlannedDurationMinutes,
    TimingMode TimingMode,
    ScheduleType ScheduleType,
    TaskImportance Importance)
{
    public bool TryCreateTask(
        TaskId taskId,
        string? nextPhysicalAction,
        TaskState state,
        out DomainTask? task,
        out string errorMessage)
    {
        task = null;

        if (string.IsNullOrWhiteSpace(FullTitle))
        {
            errorMessage = "Enter a full task title.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ShortFocusLabel))
        {
            errorMessage = "Enter the short focus label shown during focus.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DefinitionOfDone))
        {
            errorMessage = "Enter a clear definition of done.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(FirstPhysicalAction))
        {
            errorMessage = "Enter the first physical action.";
            return false;
        }

        if (PlannedStart is null
            || PlannedStart < TimeSpan.Zero
            || PlannedStart >= TimeSpan.FromDays(1))
        {
            errorMessage = "Choose a valid planned start time.";
            return false;
        }

        if (!double.IsFinite(PlannedDurationMinutes)
            || PlannedDurationMinutes <= 0
            || PlannedDurationMinutes >= TimeSpan.MaxValue.TotalMinutes)
        {
            errorMessage = "Enter a planned duration greater than zero minutes.";
            return false;
        }

        try
        {
            task = new DomainTask(
                taskId,
                FullTitle,
                ShortFocusLabel,
                DefinitionOfDone,
                FirstPhysicalAction,
                nextPhysicalAction,
                TimeOnly.FromTimeSpan(PlannedStart.Value),
                TimeSpan.FromMinutes(PlannedDurationMinutes),
                TimingMode,
                ScheduleType,
                Importance,
                state);
            errorMessage = string.Empty;
            return true;
        }
        catch (ArgumentException exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }
}
