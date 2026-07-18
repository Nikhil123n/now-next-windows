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
