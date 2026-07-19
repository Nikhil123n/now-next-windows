using System.Globalization;
using Microsoft.Data.Sqlite;
using NowNext.Core.Domain;
using NowNext.Core.Planning;
using NowNext.Core.Sessions;

namespace NowNext.App.Persistence;

public sealed partial class TodayPlanStore
{
    public async System.Threading.Tasks.Task<WorkdaySnapshot> LoadWorkdaySnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        TodayPlan plan = await LoadTodayPlanAsync(cancellationToken);
        DaySettings? settings = await LoadDaySettingsAsync(plan.Date, cancellationToken);
        DayClosure? closure = await LoadDayClosureAsync(plan.Date, cancellationToken);
        long revision = await LoadScheduleRevisionAsync(plan.Date, cancellationToken);
        return new WorkdaySnapshot(plan, revision, settings, closure);
    }

    public async System.Threading.Tasks.Task SaveDaySettingsAsync(
        DaySettings settings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateOnly planDate = CaptureToday();
        if (settings.Date != planDate)
        {
            throw new InvalidOperationException("Day settings can be changed only for today.");
        }

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsurePlanExistsAsync(connection, transaction, planDate, cancellationToken);
            await EnsureDayOpenAsync(connection, transaction, planDate, cancellationToken);
            if (settings.DailyWinTaskId is { } dailyWinTaskId)
            {
                await EnsureScheduledTaskExistsAsync(
                    connection,
                    transaction,
                    planDate,
                    dailyWinTaskId,
                    cancellationToken);
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO day_settings(plan_date, shutdown_time, daily_win_task_id)
                VALUES ($planDate, $shutdownTime, $dailyWinTaskId)
                ON CONFLICT(plan_date) DO UPDATE SET
                    shutdown_time = excluded.shutdown_time,
                    daily_win_task_id = excluded.daily_win_task_id;
                """;
            command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
            command.Parameters.AddWithValue(
                "$shutdownTime",
                settings.ShutdownTime.ToString(TimeFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue(
                "$dailyWinTaskId",
                settings.DailyWinTaskId is null
                    ? DBNull.Value
                    : FormatTaskId(settings.DailyWinTaskId.Value));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await IncrementScheduleRevisionAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("save today's protected settings", exception);
        }
    }

    public async System.Threading.Tasks.Task ApplyScheduleRepairAsync(
        ScheduleRepairProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(proposal);
        if (!proposal.CanApply)
        {
            throw new InvalidOperationException("Only a feasible repair with changes can be applied.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateOnly planDate = CaptureToday();
        if (proposal.Request.Plan.Date != planDate)
        {
            throw new InvalidOperationException("A schedule repair can be applied only on its date.");
        }

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureDayOpenAsync(connection, transaction, planDate, cancellationToken);
            await ValidateRepairBaseAsync(
                connection,
                transaction,
                proposal,
                cancellationToken);
            IReadOnlyList<ScheduleEntry> entries = await ReadPlanEntriesAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            Dictionary<TaskId, ScheduleMove> moves = proposal.Moves.ToDictionary(move => move.TaskId);
            Dictionary<TaskId, int> revisedPositions = proposal.RevisedTaskOrder
                .Select((taskId, position) => (taskId, position))
                .ToDictionary(item => item.taskId, item => item.position);
            if (revisedPositions.Count != entries.Count
                || entries.Any(entry => !revisedPositions.ContainsKey(entry.Task.Id)))
            {
                throw new InvalidOperationException(
                    "The schedule repair does not contain today's complete task order.");
            }

            DateTimeOffset acceptedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            await InsertRepairHeaderAsync(
                connection,
                transaction,
                proposal,
                planDate,
                acceptedAtUtc,
                cancellationToken);
            var changes = new List<RepairChange>();
            foreach (ScheduleEntry entry in entries)
            {
                TimeOnly revisedStart = moves.TryGetValue(entry.Task.Id, out ScheduleMove? move)
                    ? move.RevisedStart
                    : entry.Task.PlannedStart;
                TaskState revisedState = entry.Task.Id == proposal.Deferral?.TaskId
                    ? TaskState.Deferred
                    : entry.Task.State;
                int revisedPosition = revisedPositions[entry.Task.Id];
                if (revisedStart != entry.Task.PlannedStart
                    || revisedState != entry.Task.State
                    || revisedPosition != entry.Position)
                {
                    if (revisedStart != entry.Task.PlannedStart
                        && entry.Task.ScheduleType != ScheduleType.Flexible)
                    {
                        throw new InvalidOperationException(
                            $"Schedule repair attempted to move Fixed task '{entry.Task.Id}'.");
                    }

                    changes.Add(new RepairChange(
                        entry.Task.Id,
                        entry.Task.PlannedStart,
                        revisedStart,
                        entry.Position,
                        revisedPosition,
                        entry.Task.State,
                        revisedState));
                }
            }

            if (changes.Count == 0)
            {
                throw new InvalidOperationException("The schedule repair contains no persisted change.");
            }

            for (int ordinal = 0; ordinal < changes.Count; ordinal++)
            {
                await InsertRepairChangeAsync(
                    connection,
                    transaction,
                    proposal.Request.Id,
                    changes[ordinal],
                    ordinal,
                    cancellationToken);
                await UpdateRepairTaskAsync(
                    connection,
                    transaction,
                    changes[ordinal].TaskId,
                    changes[ordinal].RevisedStart,
                    changes[ordinal].RevisedState,
                    cancellationToken);
            }

            if (changes.Any(change => change.PreviousPosition != change.RevisedPosition))
            {
                await OffsetPositionsAsync(
                    connection,
                    transaction,
                    planDate,
                    entries.Count,
                    cancellationToken);
                foreach ((TaskId taskId, int position) in revisedPositions)
                {
                    await SetPositionAsync(
                        connection,
                        transaction,
                        planDate,
                        taskId,
                        position,
                        cancellationToken);
                }
            }

            foreach (ProtectedFixedCommitment protection in proposal.ProtectedFixedCommitments)
            {
                await InsertRepairProtectionAsync(
                    connection,
                    transaction,
                    proposal.Request.Id,
                    protection,
                    cancellationToken);
            }

            await IncrementScheduleRevisionAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("apply the approved schedule repair", exception);
        }
    }

    public async System.Threading.Tasks.Task<bool> UndoLatestScheduleRepairAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateOnly planDate = CaptureToday();

        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureDayOpenAsync(connection, transaction, planDate, cancellationToken);
            string? repairId = await LoadLatestUndoableRepairIdAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            if (repairId is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            IReadOnlyList<RepairChange> changes = await LoadRepairChangesAsync(
                connection,
                transaction,
                repairId,
                cancellationToken);
            IReadOnlyList<ScheduleEntry> entries = await ReadPlanEntriesAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            Dictionary<TaskId, ScheduleEntry> byTask = entries.ToDictionary(entry => entry.Task.Id);
            foreach (RepairChange change in changes)
            {
                if (!byTask.TryGetValue(change.TaskId, out ScheduleEntry? entry)
                    || entry.Task.PlannedStart != change.RevisedStart
                    || entry.Task.State != change.RevisedState
                    || entry.Position != change.RevisedPosition)
                {
                    throw new InvalidOperationException(
                        "The latest schedule repair cannot be undone after its affected tasks changed.");
                }
            }

            foreach (RepairChange change in changes)
            {
                await UpdateRepairTaskAsync(
                    connection,
                    transaction,
                    change.TaskId,
                    change.PreviousStart,
                    change.PreviousState,
                    cancellationToken);
            }

            if (changes.Any(change => change.PreviousPosition != change.RevisedPosition))
            {
                await OffsetPositionsAsync(
                    connection,
                    transaction,
                    planDate,
                    entries.Count,
                    cancellationToken);
                Dictionary<TaskId, int> restoredPositions = entries.ToDictionary(
                    entry => entry.Task.Id,
                    entry => changes.SingleOrDefault(change => change.TaskId == entry.Task.Id)
                        ?.PreviousPosition ?? entry.Position);
                foreach ((TaskId taskId, int position) in restoredPositions)
                {
                    await SetPositionAsync(
                        connection,
                        transaction,
                        planDate,
                        taskId,
                        position,
                        cancellationToken);
                }
            }

            await using (SqliteCommand markUndone = connection.CreateCommand())
            {
                markUndone.Transaction = transaction;
                markUndone.CommandText =
                    """
                    UPDATE schedule_repairs
                    SET undone_at_utc = $undoneAtUtc
                    WHERE repair_id = $repairId
                      AND undone_at_utc IS NULL;
                    """;
                markUndone.Parameters.AddWithValue(
                    "$undoneAtUtc",
                    FormatTimestamp(_timeProvider.GetUtcNow()));
                markUndone.Parameters.AddWithValue("$repairId", repairId);
                if (await markUndone.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("The latest schedule repair was already undone.");
                }
            }

            await IncrementScheduleRevisionAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("undo the latest schedule repair", exception);
        }
    }

    public async System.Threading.Tasks.Task<ShutdownSummary> CreateShutdownSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        TodayPlan plan = await LoadTodayPlanAsync(cancellationToken);
        DaySettings? settings = await LoadDaySettingsAsync(plan.Date, cancellationToken);
        IReadOnlyList<SessionActual> actuals = await LoadSessionActualsAsync(
            plan.Date,
            cancellationToken);
        IReadOnlyDictionary<TaskId, string> actions = await LoadLatestNextActionsAsync(
            plan.Date,
            cancellationToken);
        return WorkdayProjections.CreateShutdownSummary(
            plan,
            actuals,
            settings?.DailyWinTaskId,
            actions);
    }

    public async System.Threading.Tasks.Task<DayClosure> CloseDayAsync(
        ShutdownSummary summary,
        SessionCheckpoint? dayClosedCheckpoint = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(summary);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        DateOnly planDate = CaptureToday();
        if (summary.Date != planDate)
        {
            throw new InvalidOperationException("Only today's workday can be closed.");
        }

        if (dayClosedCheckpoint is not null
            && dayClosedCheckpoint.State != SessionCheckpointState.DayClosed)
        {
            throw new ArgumentException(
                "A day-closure checkpoint must be in DayClosed state.",
                nameof(dayClosedCheckpoint));
        }

        DateTimeOffset closedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureDayOpenAsync(connection, transaction, planDate, cancellationToken);
            await EnsureDaySettingsExistAsync(
                connection,
                transaction,
                planDate,
                cancellationToken);
            if (dayClosedCheckpoint is not null)
            {
                await EnsureScheduledTaskExistsAsync(
                    connection,
                    transaction,
                    planDate,
                    dayClosedCheckpoint.TaskId,
                    cancellationToken);
                await EnsureCurrentSlotCanBeReplacedAsync(
                    connection,
                    transaction,
                    dayClosedCheckpoint.Id,
                    cancellationToken);
                await UpsertCurrentSessionAsync(
                    connection,
                    transaction,
                    dayClosedCheckpoint,
                    cancellationToken);
                await UpsertFocusSessionRecordAsync(
                    connection,
                    transaction,
                    planDate,
                    dayClosedCheckpoint,
                    cancellationToken);
                _ = await UpdateTaskFromSessionAsync(
                    connection,
                    transaction,
                    dayClosedCheckpoint,
                    cancellationToken);
            }

            await InsertDayClosureAsync(
                connection,
                transaction,
                summary,
                closedAtUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreateDayClosure(summary, closedAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw CreateOperationException("close today's workday", exception);
        }
    }

    public System.Threading.Tasks.Task<DayClosure?> LoadDayClosureAsync(
        CancellationToken cancellationToken = default)
    {
        return LoadDayClosureAsync(CaptureToday(), cancellationToken);
    }

    private async System.Threading.Tasks.Task<DaySettings?> LoadDaySettingsAsync(
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT shutdown_time, daily_win_task_id
            FROM day_settings
            WHERE plan_date = $planDate;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        TaskId? dailyWin = reader.IsDBNull(1) ? null : ParseTaskId(reader.GetString(1));
        return new DaySettings(planDate, ParseTime(reader.GetString(0), "day settings"), dailyWin);
    }

    private async System.Threading.Tasks.Task<long> LoadScheduleRevisionAsync(
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT schedule_revision
            FROM today_plans
            WHERE plan_date = $planDate;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async System.Threading.Tasks.Task<DayClosure?> LoadDayClosureAsync(
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT closed_at_utc,
                   total_planned_ticks,
                   total_actual_ticks,
                   daily_win_task_id,
                   daily_win_status,
                   next_unfinished_task_id,
                   next_physical_action
            FROM day_closures
            WHERE plan_date = $planDate;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        DateTimeOffset closedAt = ParseUtcTimestamp(
            reader.GetString(0),
            "day closure",
            FormatDate(planDate));
        TimeSpan planned = TimeSpan.FromTicks(reader.GetInt64(1));
        TimeSpan actual = TimeSpan.FromTicks(reader.GetInt64(2));
        TaskId? dailyWin = reader.IsDBNull(3) ? null : ParseTaskId(reader.GetString(3));
        DailyWinStatus status = ParseEnum<DailyWinStatus>(reader.GetString(4), "day closure");
        TaskId? next = reader.IsDBNull(5) ? null : ParseTaskId(reader.GetString(5));
        string? action = reader.IsDBNull(6) ? null : reader.GetString(6);
        await reader.DisposeAsync();

        await using SqliteCommand itemCommand = connection.CreateCommand();
        itemCommand.CommandText =
            """
            SELECT task_id, outcome, planned_duration_ticks, actual_duration_ticks
            FROM day_closure_items
            WHERE plan_date = $planDate
            ORDER BY rowid;
            """;
        itemCommand.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        var items = new List<DayClosureItem>();
        await using SqliteDataReader itemReader = await itemCommand.ExecuteReaderAsync(cancellationToken);
        while (await itemReader.ReadAsync(cancellationToken))
        {
            items.Add(new DayClosureItem(
                ParseTaskId(itemReader.GetString(0)),
                ParseEnum<TaskState>(itemReader.GetString(1), "day closure item"),
                TimeSpan.FromTicks(itemReader.GetInt64(2)),
                TimeSpan.FromTicks(itemReader.GetInt64(3))));
        }

        return new DayClosure(
            planDate,
            closedAt,
            planned,
            actual,
            dailyWin,
            status,
            next,
            action,
            items);
    }

    private static async System.Threading.Tasks.Task ValidateRepairBaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ScheduleRepairProposal proposal,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT p.schedule_revision, s.shutdown_time
            FROM today_plans AS p
            INNER JOIN day_settings AS s ON s.plan_date = p.plan_date
            WHERE p.plan_date = $planDate;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(proposal.Request.Plan.Date));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Configure today's protected shutdown time before applying a repair.");
        }

        if (reader.GetInt64(0) != proposal.Request.BaseRevision
            || ParseTime(reader.GetString(1), "repair shutdown") != proposal.Request.ShutdownTime)
        {
            throw new InvalidOperationException(
                "The schedule changed after this proposal was created. Review a new repair.");
        }
    }

    private static async System.Threading.Tasks.Task EnsureDaySettingsExistAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM day_settings WHERE plan_date = $planDate);";
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(result, CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException(
                "Configure today's protected shutdown time before closing the workday.");
        }
    }

    private static async System.Threading.Tasks.Task<IReadOnlyList<ScheduleEntry>> ReadPlanEntriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
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

        return entries;
    }

    private static async System.Threading.Tasks.Task InsertRepairHeaderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ScheduleRepairProposal proposal,
        DateOnly planDate,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO schedule_repairs(
                repair_id,
                plan_date,
                trigger_kind,
                trigger_observed_at_utc,
                extension_ticks,
                base_revision,
                shutdown_time,
                buffer_consumed_ticks,
                revised_finish_ticks,
                accepted_at_utc,
                undone_at_utc)
            VALUES (
                $repairId,
                $planDate,
                $triggerKind,
                $observedAtUtc,
                $extensionTicks,
                $baseRevision,
                $shutdownTime,
                $bufferConsumedTicks,
                $revisedFinishTicks,
                $acceptedAtUtc,
                NULL);
            """;
        command.Parameters.AddWithValue("$repairId", proposal.Request.Id.ToString());
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        command.Parameters.AddWithValue("$triggerKind", proposal.Request.Trigger.Kind.ToString());
        command.Parameters.AddWithValue(
            "$observedAtUtc",
            FormatTimestamp(proposal.Request.Trigger.ObservedAtUtc));
        command.Parameters.AddWithValue(
            "$extensionTicks",
            proposal.Request.Trigger.Extension is null
                ? DBNull.Value
                : proposal.Request.Trigger.Extension.Value.Ticks);
        command.Parameters.AddWithValue("$baseRevision", proposal.Request.BaseRevision);
        command.Parameters.AddWithValue(
            "$shutdownTime",
            proposal.Request.ShutdownTime.ToString(TimeFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$bufferConsumedTicks", proposal.BufferConsumed.Ticks);
        command.Parameters.AddWithValue(
            "$revisedFinishTicks",
            proposal.RevisedFinishFromMidnight.Ticks);
        command.Parameters.AddWithValue("$acceptedAtUtc", FormatTimestamp(acceptedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task InsertRepairChangeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ScheduleRepairId repairId,
        RepairChange change,
        int ordinal,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO schedule_repair_changes(
                repair_id,
                task_id,
                ordinal,
                previous_start,
                revised_start,
                previous_position,
                revised_position,
                previous_state,
                revised_state)
            VALUES (
                $repairId,
                $taskId,
                $ordinal,
                $previousStart,
                $revisedStart,
                $previousPosition,
                $revisedPosition,
                $previousState,
                $revisedState);
            """;
        command.Parameters.AddWithValue("$repairId", repairId.ToString());
        command.Parameters.AddWithValue("$taskId", FormatTaskId(change.TaskId));
        command.Parameters.AddWithValue("$ordinal", ordinal);
        command.Parameters.AddWithValue(
            "$previousStart",
            change.PreviousStart.ToString(TimeFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$revisedStart",
            change.RevisedStart.ToString(TimeFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$previousPosition", change.PreviousPosition);
        command.Parameters.AddWithValue("$revisedPosition", change.RevisedPosition);
        command.Parameters.AddWithValue("$previousState", change.PreviousState.ToString());
        command.Parameters.AddWithValue("$revisedState", change.RevisedState.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task UpdateRepairTaskAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TaskId taskId,
        TimeOnly start,
        TaskState state,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE tasks
            SET planned_start = $plannedStart,
                task_state = $taskState
            WHERE task_id = $taskId
              AND deleted_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "$plannedStart",
            start.ToString(TimeFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$taskState", state.ToString());
        command.Parameters.AddWithValue("$taskId", FormatTaskId(taskId));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException($"Repair task '{taskId}' is no longer available.");
        }
    }

    private static async System.Threading.Tasks.Task InsertRepairProtectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ScheduleRepairId repairId,
        ProtectedFixedCommitment protection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO schedule_repair_protections(
                repair_id, task_id, planned_start, planned_duration_ticks)
            VALUES ($repairId, $taskId, $plannedStart, $plannedDurationTicks);
            """;
        command.Parameters.AddWithValue("$repairId", repairId.ToString());
        command.Parameters.AddWithValue("$taskId", FormatTaskId(protection.TaskId));
        command.Parameters.AddWithValue(
            "$plannedStart",
            protection.Start.ToString(TimeFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$plannedDurationTicks", protection.Duration.Ticks);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task<string?> LoadLatestUndoableRepairIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT repair_id, undone_at_utc
            FROM schedule_repairs
            WHERE plan_date = $planDate
            ORDER BY accepted_at_utc DESC, repair_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !reader.IsDBNull(1))
        {
            return null;
        }

        return reader.GetString(0);
    }

    private static async System.Threading.Tasks.Task<IReadOnlyList<RepairChange>>
        LoadRepairChangesAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string repairId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT task_id,
                   previous_start,
                   revised_start,
                   previous_position,
                   revised_position,
                   previous_state,
                   revised_state
            FROM schedule_repair_changes
            WHERE repair_id = $repairId
            ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$repairId", repairId);
        var changes = new List<RepairChange>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(new RepairChange(
                ParseTaskId(reader.GetString(0)),
                ParseTime(reader.GetString(1), repairId),
                ParseTime(reader.GetString(2), repairId),
                reader.GetInt32(3),
                reader.GetInt32(4),
                ParseEnum<TaskState>(reader.GetString(5), repairId),
                ParseEnum<TaskState>(reader.GetString(6), repairId)));
        }

        return changes;
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<SessionActual>> LoadSessionActualsAsync(
        DateOnly planDate,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT task_id, committed_active_ticks
            FROM focus_session_records
            WHERE plan_date = $planDate;
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        var actuals = new List<SessionActual>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actuals.Add(new SessionActual(
                ParseTaskId(reader.GetString(0)),
                TimeSpan.FromTicks(reader.GetInt64(1))));
        }

        return actuals;
    }

    private async System.Threading.Tasks.Task<IReadOnlyDictionary<TaskId, string>>
        LoadLatestNextActionsAsync(
            DateOnly planDate,
            CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.task_id, c.next_physical_action
            FROM context_capsules AS c
            INNER JOIN schedule_entries AS e ON e.task_id = c.task_id
            WHERE e.plan_date = $planDate
              AND c.saved_at_utc = (
                  SELECT MAX(latest.saved_at_utc)
                  FROM context_capsules AS latest
                  WHERE latest.task_id = c.task_id);
            """;
        command.Parameters.AddWithValue("$planDate", FormatDate(planDate));
        var actions = new Dictionary<TaskId, string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actions[ParseTaskId(reader.GetString(0))] = reader.GetString(1);
        }

        return actions;
    }

    private static async System.Threading.Tasks.Task InsertDayClosureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ShutdownSummary summary,
        DateTimeOffset closedAtUtc,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO day_closures(
                    plan_date,
                    closed_at_utc,
                    total_planned_ticks,
                    total_actual_ticks,
                    daily_win_task_id,
                    daily_win_status,
                    next_unfinished_task_id,
                    next_physical_action)
                VALUES (
                    $planDate,
                    $closedAtUtc,
                    $totalPlannedTicks,
                    $totalActualTicks,
                    $dailyWinTaskId,
                    $dailyWinStatus,
                    $nextUnfinishedTaskId,
                    $nextPhysicalAction);
                """;
            command.Parameters.AddWithValue("$planDate", FormatDate(summary.Date));
            command.Parameters.AddWithValue("$closedAtUtc", FormatTimestamp(closedAtUtc));
            command.Parameters.AddWithValue(
                "$totalPlannedTicks",
                summary.TotalPlannedDuration.Ticks);
            command.Parameters.AddWithValue(
                "$totalActualTicks",
                summary.TotalActualDuration.Ticks);
            command.Parameters.AddWithValue(
                "$dailyWinTaskId",
                summary.DailyWinTaskId is null
                    ? DBNull.Value
                    : FormatTaskId(summary.DailyWinTaskId.Value));
            command.Parameters.AddWithValue(
                "$dailyWinStatus",
                summary.DailyWinStatus.ToString());
            command.Parameters.AddWithValue(
                "$nextUnfinishedTaskId",
                summary.NextUnfinishedTaskId is null
                    ? DBNull.Value
                    : FormatTaskId(summary.NextUnfinishedTaskId.Value));
            command.Parameters.AddWithValue(
                "$nextPhysicalAction",
                (object?)summary.NextPhysicalAction ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        IEnumerable<(ShutdownTaskSummary Item, TaskState State)> items =
            summary.Completed.Select(item => (item, TaskState.Completed))
                .Concat(summary.Deferred.Select(item => (item, TaskState.Deferred)));
        foreach ((ShutdownTaskSummary item, TaskState state) in items)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO day_closure_items(
                    plan_date,
                    task_id,
                    outcome,
                    planned_duration_ticks,
                    actual_duration_ticks)
                VALUES (
                    $planDate,
                    $taskId,
                    $outcome,
                    $plannedDurationTicks,
                    $actualDurationTicks);
                """;
            command.Parameters.AddWithValue("$planDate", FormatDate(summary.Date));
            command.Parameters.AddWithValue("$taskId", FormatTaskId(item.TaskId));
            command.Parameters.AddWithValue("$outcome", state.ToString());
            command.Parameters.AddWithValue(
                "$plannedDurationTicks",
                item.PlannedDuration.Ticks);
            command.Parameters.AddWithValue(
                "$actualDurationTicks",
                item.ActualDuration.Ticks);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static DayClosure CreateDayClosure(
        ShutdownSummary summary,
        DateTimeOffset closedAtUtc)
    {
        DayClosureItem[] items = summary.Completed
            .Select(item => new DayClosureItem(
                item.TaskId,
                TaskState.Completed,
                item.PlannedDuration,
                item.ActualDuration))
            .Concat(summary.Deferred.Select(item => new DayClosureItem(
                item.TaskId,
                TaskState.Deferred,
                item.PlannedDuration,
                item.ActualDuration)))
            .ToArray();
        return new DayClosure(
            summary.Date,
            closedAtUtc,
            summary.TotalPlannedDuration,
            summary.TotalActualDuration,
            summary.DailyWinTaskId,
            summary.DailyWinStatus,
            summary.NextUnfinishedTaskId,
            summary.NextPhysicalAction,
            items);
    }

    private static TaskId ParseTaskId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out Guid parsed))
        {
            throw new InvalidDataException("Stored task ID is invalid.");
        }

        return new TaskId(parsed);
    }

    private sealed record RepairChange(
        TaskId TaskId,
        TimeOnly PreviousStart,
        TimeOnly RevisedStart,
        int PreviousPosition,
        int RevisedPosition,
        TaskState PreviousState,
        TaskState RevisedState);
}
