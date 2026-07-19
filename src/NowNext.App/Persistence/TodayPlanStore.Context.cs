using Microsoft.Data.Sqlite;
using NowNext.Core.Domain;
using NowNext.Core.Sessions;

namespace NowNext.App.Persistence;

public sealed partial class TodayPlanStore
{
    public async System.Threading.Tasks.Task<ContextCapsule?> LoadLatestContextCapsuleAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateTaskId(taskId, nameof(taskId));
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT session_id,
                       next_physical_action,
                       note,
                       saved_at_utc
                FROM context_capsules
                WHERE task_id = $taskId
                ORDER BY saved_at_utc DESC, session_id DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            string sessionIdText = reader.GetString(0);
            if (!Guid.TryParseExact(sessionIdText, "D", out Guid sessionId))
            {
                throw new InvalidDataException(
                    $"Stored Context Capsule for task '{taskId}' has an invalid session ID.");
            }

            try
            {
                return new ContextCapsule(
                    taskId,
                    new SessionId(sessionId),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    ParseUtcTimestamp(
                        reader.GetString(3),
                        "Context Capsule timestamp",
                        sessionIdText));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Stored Context Capsule for task '{taskId}' is invalid.",
                    exception);
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
            throw CreateOperationException("load a Context Capsule", exception);
        }
    }

    public async System.Threading.Tasks.Task<BreakSettings> LoadBreakSettingsAsync(
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
                SELECT default_duration_ticks, user_selected_movement
                FROM break_settings
                WHERE slot = 1;
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return BreakSettings.CreateDefault();
            }

            try
            {
                return new BreakSettings(
                    TimeSpan.FromTicks(reader.GetInt64(0)),
                    reader.IsDBNull(1) ? null : reader.GetString(1));
            }
            catch (Exception exception) when (
                exception is ArgumentException or OverflowException)
            {
                throw new InvalidDataException("Stored Break settings are invalid.", exception);
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
            throw CreateOperationException("load Break settings", exception);
        }
    }

    public async System.Threading.Tasks.Task SaveBreakSettingsAsync(
        BreakSettings settings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO break_settings(
                    slot,
                    default_duration_ticks,
                    user_selected_movement)
                VALUES (1, $defaultDurationTicks, $userSelectedMovement)
                ON CONFLICT(slot) DO UPDATE SET
                    default_duration_ticks = excluded.default_duration_ticks,
                    user_selected_movement = excluded.user_selected_movement;
                """;
            command.Parameters.AddWithValue(
                "$defaultDurationTicks",
                settings.DefaultBreakDuration.Ticks);
            command.Parameters.AddWithValue(
                "$userSelectedMovement",
                (object?)settings.UserSelectedMovement ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("save Break settings", exception);
        }
    }

    private static async System.Threading.Tasks.Task UpsertContextCapsuleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ContextCapsule capsule,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO context_capsules(
                session_id,
                task_id,
                next_physical_action,
                note,
                saved_at_utc)
            VALUES (
                $sessionId,
                $taskId,
                $nextPhysicalAction,
                $note,
                $savedAtUtc)
            ON CONFLICT(session_id) DO UPDATE SET
                task_id = excluded.task_id,
                next_physical_action = excluded.next_physical_action,
                note = excluded.note,
                saved_at_utc = excluded.saved_at_utc;
            """;
        command.Parameters.AddWithValue("$sessionId", FormatSessionId(capsule.SessionId));
        command.Parameters.AddWithValue("$taskId", FormatTaskId(capsule.TaskId));
        command.Parameters.AddWithValue("$nextPhysicalAction", capsule.NextPhysicalAction);
        command.Parameters.AddWithValue("$note", (object?)capsule.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$savedAtUtc", FormatTimestamp(capsule.SavedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
