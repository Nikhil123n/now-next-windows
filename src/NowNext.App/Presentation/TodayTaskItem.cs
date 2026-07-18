using System.Globalization;
using NowNext.Core.Domain;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.App.Presentation;

public sealed class TodayTaskItem
{
    public TodayTaskItem(ScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    public ScheduleEntry Entry { get; }

    public DomainTask Task => Entry.Task;

    public string PlannedStartText => Task.PlannedStart.ToString("t", CultureInfo.CurrentCulture);

    public string DurationText => Task.PlannedDuration.TotalMinutes == 1
        ? "1 minute"
        : string.Create(
            CultureInfo.CurrentCulture,
            $"{Task.PlannedDuration.TotalMinutes:0.#} minutes");

    public string ScheduleText => Task.ScheduleType == ScheduleType.Fixed
        ? "Fixed — protected time"
        : "Flexible — movable time";

    public string TimingText => Task.TimingMode == TimingMode.CountUp
        ? "Count up"
        : "Countdown";

    public string StartAccessibilityName => $"Start {Task.ShortFocusLabel}";

    public string EditAccessibilityName => $"Edit {Task.FullTitle}";

    public string DeleteAccessibilityName => $"Delete {Task.FullTitle}";

    public string MoveEarlierAccessibilityName => $"Move {Task.FullTitle} earlier";

    public string MoveLaterAccessibilityName => $"Move {Task.FullTitle} later";
}
