using System.Globalization;
using NowNext.Core.Sessions;

namespace NowNext.App.Presentation;

public sealed record TimerDisplayParts(
    string Prefix,
    string Hours,
    string Minutes,
    string Seconds,
    bool ShowHours,
    string AccessibleText);

public static class TimerDisplayFormatter
{
    public static TimerDisplayParts Format(SessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return view.Timer switch
        {
            CountUpTimerReading countUp => FormatDuration(countUp.Elapsed, string.Empty, false),
            CountdownTimerReading countdown => FormatDuration(
                countdown.Remaining,
                string.Empty,
                true),
            OvertimeTimerReading overtime => FormatDuration(overtime.Overtime, "+", false),
            LandingTimerReading landing => FormatDuration(landing.Elapsed, string.Empty, false),
            BreakTimerReading @break => FormatDuration(@break.Elapsed, string.Empty, false),
            NoTimerReading => FormatDuration(TimeSpan.Zero, string.Empty, false),
            _ => throw new InvalidOperationException("The timer reading is not supported."),
        };
    }

    private static TimerDisplayParts FormatDuration(
        TimeSpan duration,
        string prefix,
        bool roundUp)
    {
        double totalSeconds = Math.Max(0, duration.TotalSeconds);
        long wholeSeconds = roundUp
            ? checked((long)Math.Ceiling(totalSeconds))
            : checked((long)Math.Floor(totalSeconds));
        long hours = wholeSeconds / 3600;
        long minutes = (wholeSeconds % 3600) / 60;
        long seconds = wholeSeconds % 60;
        bool showHours = hours > 0;
        string hoursText = hours.ToString(CultureInfo.InvariantCulture);
        string minutesText = showHours
            ? minutes.ToString("00", CultureInfo.InvariantCulture)
            : (wholeSeconds / 60).ToString("00", CultureInfo.InvariantCulture);
        string secondsText = seconds.ToString("00", CultureInfo.InvariantCulture);
        string accessibleText = prefix.Length == 0
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{wholeSeconds / 60} minutes {seconds} seconds")
            : string.Create(
                CultureInfo.CurrentCulture,
                $"overtime {wholeSeconds / 60} minutes {seconds} seconds");

        return new TimerDisplayParts(
            prefix,
            hoursText,
            minutesText,
            secondsText,
            showHours,
            accessibleText);
    }
}
