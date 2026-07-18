using Microsoft.Data.Sqlite;
using NowNext.Core.Domain;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests;

internal static class TestTaskFactory
{
    internal static DomainTask Create(
        TaskId? id = null,
        string fullTitle = "Prepare the project brief",
        string shortFocusLabel = "Project brief",
        string definitionOfDone = "The brief is ready for review",
        string firstPhysicalAction = "Open the brief document",
        string? nextPhysicalAction = null,
        TimeOnly? plannedStart = null,
        TimeSpan? plannedDuration = null,
        TimingMode timingMode = TimingMode.CountUp,
        ScheduleType scheduleType = ScheduleType.Fixed,
        TaskImportance importance = TaskImportance.Normal,
        TaskState state = TaskState.Planned)
    {
        return new DomainTask(
            id ?? new TaskId(Guid.NewGuid()),
            fullTitle,
            shortFocusLabel,
            definitionOfDone,
            firstPhysicalAction,
            nextPhysicalAction,
            plannedStart ?? new TimeOnly(9, 15, 30, 250),
            plannedDuration ?? TimeSpan.FromMinutes(45),
            timingMode,
            scheduleType,
            importance,
            state);
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    internal FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private long _timestamp;

    internal ManualTimeProvider(
        DateTimeOffset utcNow,
        long timestamp = 0,
        long timestampFrequency = TimeSpan.TicksPerSecond)
    {
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestampFrequency),
                "Timestamp frequency must be positive.");
        }

        _utcNow = utcNow.ToUniversalTime();
        _timestamp = timestamp;
        TimestampFrequency = timestampFrequency;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long TimestampFrequency { get; }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }

    public override long GetTimestamp()
    {
        return _timestamp;
    }

    internal void Advance(TimeSpan duration)
    {
        AdvanceMonotonic(duration);
        AdjustUtc(duration);
    }

    internal void AdvanceMonotonic(TimeSpan duration)
    {
        decimal timestampDelta =
            duration.Ticks * (decimal)TimestampFrequency / TimeSpan.TicksPerSecond;
        if (timestampDelta != decimal.Truncate(timestampDelta))
        {
            throw new ArgumentException(
                "Duration must resolve to a whole timestamp unit.",
                nameof(duration));
        }

        _timestamp = checked(_timestamp + decimal.ToInt64(timestampDelta));
    }

    internal void AdjustUtc(TimeSpan duration)
    {
        _utcNow = _utcNow.Add(duration);
    }

    internal void SetTimestamp(long timestamp)
    {
        _timestamp = timestamp;
    }
}

internal sealed class TestDatabase : IDisposable
{
    private readonly string _directoryPath;

    internal TestDatabase()
    {
        _directoryPath = Path.Combine(
            Path.GetTempPath(),
            "NowNext.Core.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directoryPath);
        DatabasePath = Path.Combine(_directoryPath, "test.db");
    }

    internal string DatabasePath { get; }

    internal SqliteConnection CreateConnection(bool foreignKeys = true)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = foreignKeys,
            Pooling = false,
        };
        return new SqliteConnection(builder.ToString());
    }

    public void Dispose()
    {
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            File.Delete(DatabasePath + suffix);
        }

        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath);
        }
    }
}
