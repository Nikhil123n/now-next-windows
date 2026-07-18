namespace NowNext.Core.Sessions;

public abstract record TimerReading
{
    private protected TimerReading()
    {
    }
}

public sealed record NoTimerReading : TimerReading;

public sealed record CountUpTimerReading(TimeSpan Elapsed, TimeSpan Limit) : TimerReading;

public sealed record CountdownTimerReading(TimeSpan Remaining, TimeSpan Limit) : TimerReading;

public sealed record OvertimeTimerReading(TimeSpan Overtime) : TimerReading;

public sealed record LandingTimerReading(TimeSpan Elapsed, TimeSpan Limit) : TimerReading;

public sealed record BreakTimerReading(TimeSpan Elapsed) : TimerReading;

public sealed record SessionView(SessionStateKind State, TimerReading Timer);
