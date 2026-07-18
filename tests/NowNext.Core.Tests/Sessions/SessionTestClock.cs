namespace NowNext.Core.Tests.Sessions;

internal sealed class SessionTestClock : TimeProvider
{
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public SessionTestClock(
        DateTimeOffset? utcNow = null,
        long timestamp = 0,
        long timestampFrequency = TimeSpan.TicksPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);

        _utcNow = utcNow ?? new DateTimeOffset(2026, 7, 18, 9, 0, 0, TimeSpan.Zero);
        _timestamp = timestamp;
        TimestampFrequency = timestampFrequency;
    }

    public override long TimestampFrequency { get; }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }

    public override long GetTimestamp()
    {
        return _timestamp;
    }

    public void Advance(TimeSpan duration)
    {
        AdvanceMonotonic(duration);
        _utcNow = _utcNow.Add(duration);
    }

    public void AdvanceMonotonic(TimeSpan duration)
    {
        decimal timestampDelta = duration.Ticks * (decimal)TimestampFrequency
            / TimeSpan.TicksPerSecond;
        _timestamp = checked(_timestamp + decimal.ToInt64(timestampDelta));
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        _utcNow = value;
    }
}
