using System.Globalization;
using Microsoft.Data.Sqlite;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;

namespace NowNext.App.Persistence;

public sealed partial class TodayPlanStore
{
    public async System.Threading.Tasks.Task SaveCurrentSessionAsync(
        SessionCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(checkpoint);
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
                checkpoint.TaskId,
                cancellationToken);
            await EnsureCurrentSlotCanBeReplacedAsync(
                connection,
                transaction,
                checkpoint.Id,
                cancellationToken);
            await UpsertCurrentSessionAsync(
                connection,
                transaction,
                checkpoint,
                cancellationToken);
            await UpdateTaskFromSessionAsync(
                connection,
                transaction,
                checkpoint,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("save the current focus session", exception);
        }
    }

    public async System.Threading.Tasks.Task<SessionCheckpoint?> LoadCurrentSessionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT session_id,
                       task_id,
                       timing_mode,
                       original_planned_duration_ticks,
                       approved_limit_ticks,
                       session_state,
                       committed_active_ticks,
                       landing_ticks,
                       break_ticks,
                       checkpointed_at_utc,
                       resume_phase,
                       boundary_kind,
                       recovery_phase,
                       prior_outcome,
                       started_at_utc,
                       completed_at_utc,
                       parked_at_utc,
                       day_closed_at_utc,
                       parked_next_physical_action
                FROM current_session_checkpoint
                WHERE slot = 1;
                """;

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadSessionCheckpoint(reader);
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
            throw CreateOperationException("load the current focus session", exception);
        }
    }

    private static async System.Threading.Tasks.Task UpsertCurrentSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO current_session_checkpoint(
                slot,
                session_id,
                task_id,
                timing_mode,
                original_planned_duration_ticks,
                approved_limit_ticks,
                session_state,
                committed_active_ticks,
                landing_ticks,
                break_ticks,
                checkpointed_at_utc,
                resume_phase,
                boundary_kind,
                recovery_phase,
                prior_outcome,
                started_at_utc,
                completed_at_utc,
                parked_at_utc,
                day_closed_at_utc,
                parked_next_physical_action)
            VALUES (
                1,
                $sessionId,
                $taskId,
                $timingMode,
                $originalPlannedDurationTicks,
                $approvedLimitTicks,
                $sessionState,
                $committedActiveTicks,
                $landingTicks,
                $breakTicks,
                $checkpointedAtUtc,
                $resumePhase,
                $boundaryKind,
                $recoveryPhase,
                $priorOutcome,
                $startedAtUtc,
                $completedAtUtc,
                $parkedAtUtc,
                $dayClosedAtUtc,
                $parkedNextPhysicalAction)
            ON CONFLICT(slot) DO UPDATE SET
                session_id = excluded.session_id,
                task_id = excluded.task_id,
                timing_mode = excluded.timing_mode,
                original_planned_duration_ticks = excluded.original_planned_duration_ticks,
                approved_limit_ticks = excluded.approved_limit_ticks,
                session_state = excluded.session_state,
                committed_active_ticks = excluded.committed_active_ticks,
                landing_ticks = excluded.landing_ticks,
                break_ticks = excluded.break_ticks,
                checkpointed_at_utc = excluded.checkpointed_at_utc,
                resume_phase = excluded.resume_phase,
                boundary_kind = excluded.boundary_kind,
                recovery_phase = excluded.recovery_phase,
                prior_outcome = excluded.prior_outcome,
                started_at_utc = excluded.started_at_utc,
                completed_at_utc = excluded.completed_at_utc,
                parked_at_utc = excluded.parked_at_utc,
                day_closed_at_utc = excluded.day_closed_at_utc,
                parked_next_physical_action = excluded.parked_next_physical_action;
            """;
        command.Parameters.AddWithValue("$sessionId", FormatSessionId(checkpoint.Id));
        command.Parameters.AddWithValue("$taskId", FormatTaskId(checkpoint.TaskId));
        command.Parameters.AddWithValue("$timingMode", checkpoint.TimingMode.ToString());
        command.Parameters.AddWithValue(
            "$originalPlannedDurationTicks",
            checkpoint.OriginalPlannedDuration.Ticks);
        command.Parameters.AddWithValue("$approvedLimitTicks", checkpoint.ApprovedLimit.Ticks);
        command.Parameters.AddWithValue("$sessionState", checkpoint.State.ToString());
        command.Parameters.AddWithValue(
            "$committedActiveTicks",
            checkpoint.CommittedActiveDuration.Ticks);
        command.Parameters.AddWithValue("$landingTicks", checkpoint.LandingDuration.Ticks);
        command.Parameters.AddWithValue("$breakTicks", checkpoint.BreakDuration.Ticks);
        command.Parameters.AddWithValue(
            "$checkpointedAtUtc",
            FormatTimestamp(checkpoint.CheckpointedAtUtc));
        AddNullableEnumParameter(command, "$resumePhase", checkpoint.ResumePhase);
        AddNullableEnumParameter(command, "$boundaryKind", checkpoint.Boundary);
        AddNullableEnumParameter(command, "$recoveryPhase", checkpoint.RecoveryPhase);
        AddNullableEnumParameter(command, "$priorOutcome", checkpoint.PriorOutcome);
        AddNullableTimestampParameter(command, "$startedAtUtc", checkpoint.StartedAtUtc);
        AddNullableTimestampParameter(command, "$completedAtUtc", checkpoint.CompletedAtUtc);
        AddNullableTimestampParameter(command, "$parkedAtUtc", checkpoint.ParkedAtUtc);
        AddNullableTimestampParameter(command, "$dayClosedAtUtc", checkpoint.DayClosedAtUtc);
        command.Parameters.AddWithValue(
            "$parkedNextPhysicalAction",
            (object?)checkpoint.ParkedNextPhysicalAction ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task UpdateTaskFromSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        TaskState taskState = GetTaskState(checkpoint);
        bool updateNextAction = taskState == TaskState.Parked;

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE tasks
            SET task_state = $taskState,
                next_physical_action = CASE
                    WHEN $updateNextAction = 1 THEN $nextPhysicalAction
                    ELSE next_physical_action
                END
            WHERE task_id = $taskId
              AND deleted_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$taskState", taskState.ToString());
        command.Parameters.AddWithValue("$updateNextAction", updateNextAction ? 1 : 0);
        command.Parameters.AddWithValue(
            "$nextPhysicalAction",
            (object?)checkpoint.ParkedNextPhysicalAction ?? DBNull.Value);
        command.Parameters.AddWithValue("$taskId", FormatTaskId(checkpoint.TaskId));
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Task ID '{checkpoint.TaskId}' could not be updated for the current session.");
        }
    }

    private static TaskState GetTaskState(SessionCheckpoint checkpoint)
    {
        return checkpoint.State switch
        {
            SessionCheckpointState.Ready => TaskState.Planned,
            SessionCheckpointState.Paused => TaskState.Active,
            SessionCheckpointState.LimitReached => TaskState.Active,
            SessionCheckpointState.Completed => TaskState.Completed,
            SessionCheckpointState.Parked => TaskState.Parked,
            SessionCheckpointState.RecoveryRequired
                when checkpoint.RecoveryPhase == ActiveSessionPhase.Break =>
                    GetOutcomeTaskState(checkpoint.PriorOutcome),
            SessionCheckpointState.RecoveryRequired => TaskState.Active,
            SessionCheckpointState.DayClosed when checkpoint.PriorOutcome is null => TaskState.Planned,
            SessionCheckpointState.DayClosed => GetOutcomeTaskState(checkpoint.PriorOutcome),
            _ => throw new InvalidOperationException(
                $"Session checkpoint state '{checkpoint.State}' cannot update a task."),
        };
    }

    private static TaskState GetOutcomeTaskState(SessionOutcome? outcome)
    {
        return outcome switch
        {
            SessionOutcome.Completed => TaskState.Completed,
            SessionOutcome.Parked => TaskState.Parked,
            _ => throw new InvalidOperationException(
                "A session outcome is required to update the task lifecycle."),
        };
    }

    private static async System.Threading.Tasks.Task EnsureCurrentSlotCanBeReplacedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT session_id, session_state
            FROM current_session_checkpoint
            WHERE slot = 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return;
        }

        string existingSessionId = reader.GetString(0);
        string existingState = reader.GetString(1);
        if (!string.Equals(existingSessionId, FormatSessionId(sessionId), StringComparison.Ordinal)
            && IsUnresolvedCheckpointState(existingState))
        {
            throw new InvalidOperationException(
                $"Session ID '{existingSessionId}' must be resolved before another session is saved.");
        }
    }

    private static async System.Threading.Tasks.Task EnsureNoUnresolvedSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT session_state
            FROM current_session_checkpoint
            WHERE slot = 1
              AND task_id = $taskId;
            """;
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is string state && IsUnresolvedCheckpointState(state))
        {
            throw new InvalidOperationException(
                $"Task ID '{taskId}' has an unresolved focus session and cannot be deleted.");
        }
    }

    private static bool IsUnresolvedCheckpointState(string value)
    {
        if (!Enum.TryParse(value, false, out SessionCheckpointState state)
            || !Enum.IsDefined(state))
        {
            throw new InvalidDataException("The stored current session state is invalid.");
        }

        return state is SessionCheckpointState.Ready
            or SessionCheckpointState.Paused
            or SessionCheckpointState.LimitReached
            or SessionCheckpointState.RecoveryRequired;
    }

    private static SessionCheckpoint ReadSessionCheckpoint(SqliteDataReader reader)
    {
        string sessionIdText = reader.GetString(0);
        string taskIdText = reader.GetString(1);
        if (!Guid.TryParseExact(sessionIdText, "D", out Guid sessionIdValue))
        {
            throw new InvalidDataException("The stored current session ID is invalid.");
        }

        if (!Guid.TryParseExact(taskIdText, "D", out Guid taskIdValue))
        {
            throw new InvalidDataException("The stored current session task ID is invalid.");
        }

        try
        {
            return new SessionCheckpoint(
                new SessionId(sessionIdValue),
                new TaskId(taskIdValue),
                ParseEnum<TimingMode>(reader.GetString(2), sessionIdText),
                TimeSpan.FromTicks(reader.GetInt64(3)),
                TimeSpan.FromTicks(reader.GetInt64(4)),
                ParseEnum<SessionCheckpointState>(reader.GetString(5), sessionIdText),
                TimeSpan.FromTicks(reader.GetInt64(6)),
                TimeSpan.FromTicks(reader.GetInt64(7)),
                TimeSpan.FromTicks(reader.GetInt64(8)),
                ParseUtcTimestamp(reader.GetString(9), "checkpoint timestamp", sessionIdText),
                ReadNullableEnum<ActiveSessionPhase>(reader, 10, sessionIdText),
                ReadNullableEnum<SessionBoundary>(reader, 11, sessionIdText),
                ReadNullableEnum<ActiveSessionPhase>(reader, 12, sessionIdText),
                ReadNullableEnum<SessionOutcome>(reader, 13, sessionIdText),
                ReadNullableTimestamp(reader, 14, "start timestamp", sessionIdText),
                ReadNullableTimestamp(reader, 15, "completion timestamp", sessionIdText),
                ReadNullableTimestamp(reader, 16, "parking timestamp", sessionIdText),
                ReadNullableTimestamp(reader, 17, "day-close timestamp", sessionIdText),
                reader.IsDBNull(18) ? null : reader.GetString(18));
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException(
                $"Stored session '{sessionIdText}' is invalid.",
                exception);
        }
    }

    private static TEnum? ReadNullableEnum<TEnum>(
        SqliteDataReader reader,
        int ordinal,
        string sessionIdText)
        where TEnum : struct, Enum
    {
        return reader.IsDBNull(ordinal)
            ? null
            : ParseEnum<TEnum>(reader.GetString(ordinal), sessionIdText);
    }

    private static DateTimeOffset? ReadNullableTimestamp(
        SqliteDataReader reader,
        int ordinal,
        string fieldName,
        string sessionIdText)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : ParseUtcTimestamp(reader.GetString(ordinal), fieldName, sessionIdText);
    }

    private static DateTimeOffset ParseUtcTimestamp(
        string value,
        string fieldName,
        string sessionIdText)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset timestamp)
            || timestamp.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                $"Stored session '{sessionIdText}' has an invalid {fieldName}.");
        }

        return timestamp;
    }

    private static void AddNullableEnumParameter<TEnum>(
        SqliteCommand command,
        string parameterName,
        TEnum? value)
        where TEnum : struct, Enum
    {
        command.Parameters.AddWithValue(parameterName, value?.ToString() ?? (object)DBNull.Value);
    }

    private static void AddNullableTimestampParameter(
        SqliteCommand command,
        string parameterName,
        DateTimeOffset? value)
    {
        command.Parameters.AddWithValue(
            parameterName,
            value is null ? DBNull.Value : FormatTimestamp(value.Value));
    }

    private static string FormatSessionId(SessionId value)
    {
        return value.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }
}
