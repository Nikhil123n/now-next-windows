using System.Data;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using NowNext.Core.Domain;
using Windows.Storage;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.App.Persistence;

public sealed partial class TodayPlanStore : IDisposable
{
    private const string DatabaseFileName = "now-next.db";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss.fffffff";
    private const string TimestampFormat = "O";

    private static readonly Migration[] Migrations =
    [
        new(
            1,
            "initial_today_plan",
            "NowNext.App.Persistence.Migrations.0001_initial_today_plan.sql"),
        new(
            2,
            "current_focus_session_checkpoint",
            "NowNext.App.Persistence.Migrations.0002_current_focus_session_checkpoint.sql"),
        new(
            3,
            "context_capsules_and_break_recovery",
            "NowNext.App.Persistence.Migrations.0003_context_capsules_and_break_recovery.sql"),
    ];

    private readonly string _databasePath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public TodayPlanStore(string databasePath, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must not be blank.", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static TodayPlanStore CreateForCurrentUser(TimeProvider? timeProvider = null)
    {
        string databasePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, DatabaseFileName);
        return new TodayPlanStore(databasePath, timeProvider);
    }

    public async System.Threading.Tasks.Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            string? databaseDirectory = Path.GetDirectoryName(_databasePath);
            if (string.IsNullOrEmpty(databaseDirectory))
            {
                throw new TodayPlanStorageException("The local database directory is invalid.");
            }

            Directory.CreateDirectory(databaseDirectory);

            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await ApplyMigrationsAsync(connection, cancellationToken);
            _initialized = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TodayPlanStorageException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw new TodayPlanStorageException(
                "NOW/NEXT could not initialize its local database.",
                exception);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async System.Threading.Tasks.Task CreateTaskAsync(
        DomainTask task,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(task);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateOnly planDate = CaptureToday();

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await EnsurePlanExistsAsync(connection, transaction, planDate, cancellationToken);

            if (await TaskRowExistsAsync(connection, transaction, task.Id, cancellationToken))
            {
                throw new InvalidOperationException($"Task ID '{task.Id}' already exists.");
            }

            await InsertTaskAsync(connection, transaction, task, cancellationToken);
            int position = await GetNextPositionAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            await InsertScheduleEntryAsync(
                connection,
                transaction,
                planDate,
                task.Id,
                position,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("create a task", exception);
        }
    }

    public async System.Threading.Tasks.Task EditTaskAsync(
        DomainTask task,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(task);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateOnly planDate = CaptureToday();

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await EnsureScheduledTaskExistsAsync(
                connection,
                transaction,
                planDate,
                task.Id,
                cancellationToken);

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE tasks
                SET full_title = $fullTitle,
                    short_focus_label = $shortFocusLabel,
                    definition_of_done = $definitionOfDone,
                    first_physical_action = $firstPhysicalAction,
                    next_physical_action = $nextPhysicalAction,
                    planned_start = $plannedStart,
                    planned_duration_ticks = $plannedDurationTicks,
                    timing_mode = $timingMode,
                    schedule_type = $scheduleType,
                    importance = $importance,
                    task_state = $taskState
                WHERE task_id = $taskId
                  AND deleted_at_utc IS NULL;
                """;
            AddTaskParameters(command, task);
            int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows != 1)
            {
                throw new InvalidOperationException($"Task ID '{task.Id}' could not be edited.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("edit a task", exception);
        }
    }

    public async System.Threading.Tasks.Task DeleteTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateTaskId(taskId, nameof(taskId));
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateTimeOffset localNow = _timeProvider.GetLocalNow();
        DateOnly planDate = DateOnly.FromDateTime(localNow.DateTime);

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await EnsureNoUnresolvedSessionAsync(
                connection,
                transaction,
                taskId,
                cancellationToken);

            int deletedPosition = await GetScheduledPositionAsync(
                connection,
                transaction,
                planDate,
                taskId,
                cancellationToken);

            await DeleteScheduleEntryAsync(
                connection,
                transaction,
                planDate,
                taskId,
                cancellationToken);
            await CompactPositionsAsync(
                connection,
                transaction,
                planDate,
                deletedPosition,
                cancellationToken);
            await MarkTaskDeletedAsync(
                connection,
                transaction,
                taskId,
                localNow.ToUniversalTime(),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("delete a task", exception);
        }
    }

    public async System.Threading.Tasks.Task ReorderTasksAsync(
        IReadOnlyList<TaskId> orderedTaskIds,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(orderedTaskIds);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateOnly planDate = CaptureToday();

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            IReadOnlyList<TaskId> currentTaskIds = await ReadScheduledTaskIdsAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            ValidateReorder(currentTaskIds, orderedTaskIds);

            if (orderedTaskIds.Count > 0)
            {
                await OffsetPositionsAsync(
                    connection,
                    transaction,
                    planDate,
                    orderedTaskIds.Count,
                    cancellationToken);

                for (int position = 0; position < orderedTaskIds.Count; position++)
                {
                    await SetPositionAsync(
                        connection,
                        transaction,
                        planDate,
                        orderedTaskIds[position],
                        position,
                        cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("reorder tasks", exception);
        }
    }

    public async System.Threading.Tasks.Task<TodayPlan> LoadTodayPlanAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateOnly planDate = CaptureToday();

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT t.task_id,
                       t.full_title,
                       t.short_focus_label,
                       t.definition_of_done,
                       t.first_physical_action,
                       t.next_physical_action,
                       t.planned_start,
                       t.planned_duration_ticks,
                       t.timing_mode,
                       t.schedule_type,
                       t.importance,
                       t.task_state,
                       e.position
                FROM schedule_entries AS e
                INNER JOIN tasks AS t ON t.task_id = e.task_id
                WHERE e.plan_date = $planDate
                  AND t.deleted_at_utc IS NULL
                ORDER BY e.position;
                """;
            command.Parameters.AddWithValue("$planDate", FormatDate(planDate));

            var entries = new List<ScheduleEntry>();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(ReadScheduleEntry(reader));
            }

            try
            {
                return new TodayPlan(planDate, entries);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Stored schedule ordering is invalid.", exception);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("load today's plan", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _initializationLock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static async System.Threading.Tasks.Task ApplyMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (SqliteCommand createMigrationTable = connection.CreateCommand())
        {
            createMigrationTable.Transaction = transaction;
            createMigrationTable.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER NOT NULL PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_utc TEXT NOT NULL
                );
                """;
            await createMigrationTable.ExecuteNonQueryAsync(cancellationToken);
        }

        IReadOnlyList<AppliedMigration> appliedMigrations = await ReadAppliedMigrationsAsync(
            connection,
            transaction,
            cancellationToken);
        ValidateAppliedMigrations(appliedMigrations);

        for (int index = appliedMigrations.Count; index < Migrations.Length; index++)
        {
            Migration migration = Migrations[index];
            string migrationSql = await ReadMigrationSqlAsync(migration, cancellationToken);

            await using (SqliteCommand migrationCommand = connection.CreateCommand())
            {
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = migrationSql;
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using SqliteCommand recordCommand = connection.CreateCommand();
            recordCommand.Transaction = transaction;
            recordCommand.CommandText =
                """
                INSERT INTO schema_migrations(version, name, applied_utc)
                VALUES ($version, $name, $appliedUtc);
                """;
            recordCommand.Parameters.AddWithValue("$version", migration.Version);
            recordCommand.Parameters.AddWithValue("$name", migration.Name);
            recordCommand.Parameters.AddWithValue(
                "$appliedUtc",
                TimeProvider.System.GetUtcNow().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            await recordCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task<IReadOnlyList<AppliedMigration>>
        ReadAppliedMigrationsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT version, name
            FROM schema_migrations
            ORDER BY version;
            """;

        var migrations = new List<AppliedMigration>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(new AppliedMigration(reader.GetInt32(0), reader.GetString(1)));
        }

        return migrations;
    }

    private static void ValidateAppliedMigrations(IReadOnlyList<AppliedMigration> appliedMigrations)
    {
        for (int index = 0; index < appliedMigrations.Count; index++)
        {
            int expectedVersion = index + 1;
            AppliedMigration applied = appliedMigrations[index];
            if (applied.Version != expectedVersion || applied.Version > Migrations.Length)
            {
                throw new TodayPlanStorageException(
                    $"Database migration version {applied.Version} is unknown or non-contiguous.");
            }

            Migration expected = Migrations[index];
            if (!string.Equals(applied.Name, expected.Name, StringComparison.Ordinal))
            {
                throw new TodayPlanStorageException(
                    $"Database migration version {applied.Version} has an unexpected name.");
            }
        }
    }

    private static async System.Threading.Tasks.Task<string> ReadMigrationSqlAsync(
        Migration migration,
        CancellationToken cancellationToken)
    {
        Stream? stream = typeof(TodayPlanStore).Assembly.GetManifestResourceStream(
            migration.ResourceName);
        if (stream is null)
        {
            throw new TodayPlanStorageException(
                $"Embedded database migration {migration.Version} is missing.");
        }

        await using (stream)
        using (var reader = new StreamReader(stream))
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }

    private static async System.Threading.Tasks.Task EnsurePlanExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO today_plans(plan_date)
            VALUES ($planDate)
            ON CONFLICT(plan_date) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task<bool> TaskRowExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM tasks
                WHERE task_id = $taskId
            );
            """;
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private static async System.Threading.Tasks.Task InsertTaskAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DomainTask task,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO tasks(
                task_id,
                full_title,
                short_focus_label,
                definition_of_done,
                first_physical_action,
                next_physical_action,
                planned_start,
                planned_duration_ticks,
                timing_mode,
                schedule_type,
                importance,
                task_state,
                deleted_at_utc)
            VALUES (
                $taskId,
                $fullTitle,
                $shortFocusLabel,
                $definitionOfDone,
                $firstPhysicalAction,
                $nextPhysicalAction,
                $plannedStart,
                $plannedDurationTicks,
                $timingMode,
                $scheduleType,
                $importance,
                $taskState,
                NULL);
            """;
        AddTaskParameters(command, task);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddTaskParameters(SqliteCommand command, DomainTask task)
    {
        command.Parameters.AddWithValue("$taskId", FormatTaskId(task.Id));
        command.Parameters.AddWithValue("$fullTitle", task.FullTitle);
        command.Parameters.AddWithValue("$shortFocusLabel", task.ShortFocusLabel);
        command.Parameters.AddWithValue("$definitionOfDone", task.DefinitionOfDone);
        command.Parameters.AddWithValue("$firstPhysicalAction", task.FirstPhysicalAction);
        command.Parameters.AddWithValue(
            "$nextPhysicalAction",
            (object?)task.NextPhysicalAction ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$plannedStart",
            task.PlannedStart.ToString(TimeFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$plannedDurationTicks", task.PlannedDuration.Ticks);
        command.Parameters.AddWithValue("$timingMode", task.TimingMode.ToString());
        command.Parameters.AddWithValue("$scheduleType", task.ScheduleType.ToString());
        command.Parameters.AddWithValue("$importance", task.Importance.ToString());
        command.Parameters.AddWithValue("$taskState", task.State.ToString());
    }

    private static async System.Threading.Tasks.Task<int> GetNextPositionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COALESCE(MAX(position), -1) + 1
            FROM schedule_entries
            WHERE plan_date = $planDate;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async System.Threading.Tasks.Task InsertScheduleEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        TaskId taskId,
        int position,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO schedule_entries(plan_date, task_id, position)
            VALUES ($planDate, $taskId, $position);
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        command.Parameters.AddWithValue("$position", position);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task EnsureScheduledTaskExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM schedule_entries AS e
                INNER JOIN tasks AS t ON t.task_id = e.task_id
                WHERE e.plan_date = $planDate
                  AND e.task_id = $taskId
                  AND t.deleted_at_utc IS NULL
            );
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(result, CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException(
                $"Task ID '{taskId}' is not scheduled in today's plan.");
        }
    }

    private static async System.Threading.Tasks.Task<int> GetScheduledPositionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT position
            FROM schedule_entries
            WHERE plan_date = $planDate
              AND task_id = $taskId;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException(
                $"Task ID '{taskId}' is not scheduled in today's plan.");
        }

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async System.Threading.Tasks.Task DeleteScheduleEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM schedule_entries
            WHERE plan_date = $planDate
              AND task_id = $taskId;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Task ID '{taskId}' could not be removed from today's plan.");
        }
    }

    private static async System.Threading.Tasks.Task CompactPositionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        int deletedPosition,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand offsetCommand = connection.CreateCommand();
        offsetCommand.Transaction = transaction;
        offsetCommand.CommandText =
            """
            UPDATE schedule_entries
            SET position = -position - 1
            WHERE plan_date = $planDate
              AND position > $deletedPosition;
            """;
        offsetCommand.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        offsetCommand.Parameters.AddWithValue("$deletedPosition", deletedPosition);
        await offsetCommand.ExecuteNonQueryAsync(cancellationToken);

        await using SqliteCommand compactCommand = connection.CreateCommand();
        compactCommand.Transaction = transaction;
        compactCommand.CommandText =
            """
            UPDATE schedule_entries
            SET position = (-position - 1) - 1
            WHERE plan_date = $planDate
              AND position < 0;
            """;
        compactCommand.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        await compactCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task MarkTaskDeletedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TaskId taskId,
        DateTimeOffset deletedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE tasks
            SET deleted_at_utc = $deletedAtUtc
            WHERE task_id = $taskId
              AND deleted_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "$deletedAtUtc",
            deletedAtUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException($"Task ID '{taskId}' could not be marked deleted.");
        }
    }

    private static async System.Threading.Tasks.Task<IReadOnlyList<TaskId>> ReadScheduledTaskIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT task_id
            FROM schedule_entries
            WHERE plan_date = $planDate
            ORDER BY position;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));

        var taskIds = new List<TaskId>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string taskIdText = reader.GetString(0);
            if (!Guid.TryParseExact(taskIdText, "D", out Guid taskIdValue))
            {
                throw new InvalidDataException("A stored schedule task ID is invalid.");
            }

            taskIds.Add(new TaskId(taskIdValue));
        }

        return taskIds;
    }

    private static void ValidateReorder(
        IReadOnlyList<TaskId> currentTaskIds,
        IReadOnlyList<TaskId> orderedTaskIds)
    {
        if (currentTaskIds.Count != orderedTaskIds.Count)
        {
            throw new InvalidOperationException(
                "Reorder must contain every task in today's plan exactly once.");
        }

        var current = new HashSet<TaskId>(currentTaskIds);
        var seen = new HashSet<TaskId>();
        foreach (TaskId taskId in orderedTaskIds)
        {
            ValidateTaskId(taskId, nameof(orderedTaskIds));
            if (!seen.Add(taskId))
            {
                throw new InvalidOperationException(
                    $"Reorder contains duplicate task ID '{taskId}'.");
            }

            if (!current.Contains(taskId))
            {
                throw new InvalidOperationException(
                    $"Reorder contains unknown task ID '{taskId}'.");
            }
        }
    }

    private static async System.Threading.Tasks.Task OffsetPositionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        int offset,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE schedule_entries
            SET position = position + $offset
            WHERE plan_date = $planDate;
            """;
        command.Parameters.AddWithValue("$offset", offset);
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task SetPositionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        TaskId taskId,
        int position,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE schedule_entries
            SET position = $position
            WHERE plan_date = $planDate
              AND task_id = $taskId;
            """;
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Task ID '{taskId}' could not be reordered.");
        }
    }

    private static ScheduleEntry ReadScheduleEntry(SqliteDataReader reader)
    {
        string taskIdText = reader.GetString(0);
        if (!Guid.TryParseExact(taskIdText, "D", out Guid taskIdValue))
        {
            throw new InvalidDataException("A stored task ID is invalid.");
        }

        try
        {
            TimeOnly plannedStart = ParseTime(reader.GetString(6), taskIdText);
            long durationTicks = reader.GetInt64(7);
            TimingMode timingMode = ParseEnum<TimingMode>(reader.GetString(8), taskIdText);
            ScheduleType scheduleType = ParseEnum<ScheduleType>(reader.GetString(9), taskIdText);
            TaskImportance importance = ParseEnum<TaskImportance>(reader.GetString(10), taskIdText);
            TaskState taskState = ParseEnum<TaskState>(reader.GetString(11), taskIdText);

            var task = new DomainTask(
                new TaskId(taskIdValue),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                plannedStart,
                TimeSpan.FromTicks(durationTicks),
                timingMode,
                scheduleType,
                importance,
                taskState);
            return new ScheduleEntry(task, reader.GetInt32(12));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidDataException($"Stored task '{taskIdText}' is invalid.", exception);
        }
    }

    private static TimeOnly ParseTime(string value, string taskIdText)
    {
        if (!TimeOnly.TryParseExact(
                value,
                TimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly result))
        {
            throw new InvalidDataException($"Stored task '{taskIdText}' has an invalid planned start.");
        }

        return result;
    }

    private static TEnum ParseEnum<TEnum>(string value, string taskIdText)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse(value, false, out TEnum result) || !Enum.IsDefined(result))
        {
            throw new InvalidDataException(
                $"Stored task '{taskIdText}' has an invalid {typeof(TEnum).Name} value.");
        }

        return result;
    }

    private async System.Threading.Tasks.Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        };
        var connection = new SqliteConnection(connectionString.ToString());

        try
        {
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private DateOnly CaptureToday()
    {
        return DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
    }

    private static void ValidateTaskId(TaskId taskId, string parameterName)
    {
        if (taskId.Value == Guid.Empty)
        {
            throw new ArgumentException("Task ID must not be empty.", parameterName);
        }
    }

    private static string FormatDate(DateOnly value)
    {
        return value.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    private static string FormatTaskId(TaskId value)
    {
        return value.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    private static TodayPlanStorageException CreateOperationException(
        string operation,
        Exception innerException)
    {
        return new TodayPlanStorageException(
            $"NOW/NEXT could not {operation} in its local database.",
            innerException);
    }

    private static bool IsStorageFailure(Exception exception)
    {
        return exception is SqliteException or IOException or UnauthorizedAccessException;
    }

    private sealed record Migration(int Version, string Name, string ResourceName);

    private sealed record AppliedMigration(int Version, string Name);
}
