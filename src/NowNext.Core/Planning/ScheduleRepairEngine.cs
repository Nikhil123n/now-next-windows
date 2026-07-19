using NowNext.Core.Domain;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Planning;

public static class ScheduleRepairEngine
{
    private static readonly TimeSpan DayLength = TimeSpan.FromDays(1);

    public static ScheduleRepairProposal Propose(ScheduleRepairRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TimeSpan now = request.CurrentTime.ToTimeSpan();
        TimeSpan shutdown = request.ShutdownTime.ToTimeSpan();
        EntryState[] active = request.Plan.Entries
            .Where(entry => entry.Task.State is not (TaskState.Completed or TaskState.Deferred))
            .Select(entry => new EntryState(
                entry.Task,
                entry.Position,
                entry.Task.PlannedStart.ToTimeSpan(),
                entry.Task.PlannedDuration))
            .ToArray();
        ProtectedFixedCommitment[] protectedFixed = active
            .Where(entry => entry.Task.ScheduleType == ScheduleType.Fixed)
            .OrderBy(entry => entry.OriginalStart)
            .ThenBy(entry => entry.OriginalPosition)
            .Select(entry => new ProtectedFixedCommitment(
                entry.Task.Id,
                entry.Task.PlannedStart,
                entry.Duration))
            .ToArray();

        ScheduleRepairIssue issue = ValidateProtectedSchedule(
            active,
            request.CurrentTaskId,
            now,
            shutdown,
            request.CurrentTaskRemaining);
        if (issue != ScheduleRepairIssue.None)
        {
            return CreateImpossible(request, active, protectedFixed, issue, shutdown);
        }

        PlacementResult initial = PlaceFlexibleWork(request, active, deferredTaskId: null);
        ScheduleDeferral? deferral = null;
        PlacementResult result = initial;
        if (initial.RevisedFinish > shutdown)
        {
            EntryState? candidate = SelectDeferralCandidate(active, request.CurrentTaskId);
            if (candidate is not null)
            {
                deferral = new ScheduleDeferral(candidate.Task.Id);
                result = PlaceFlexibleWork(request, active, candidate.Task.Id);
            }

            if (result.RevisedFinish > shutdown)
            {
                if (result.RevisedFinish >= DayLength
                    || result.RevisedStarts.Values.Any(start => start >= DayLength))
                {
                    return CreateImpossible(
                        request,
                        active,
                        protectedFixed,
                        ScheduleRepairIssue.ScheduleCrossesMidnight,
                        shutdown);
                }

                return CreateProposal(
                    request,
                    active,
                    result,
                    ScheduleRepairStatus.Impossible,
                    ScheduleRepairIssue.InsufficientFlexibleTime,
                    deferral,
                    protectedFixed,
                    shutdown);
            }
        }

        bool changed = deferral is not null
            || result.RevisedStarts.Any(pair =>
                active.Single(entry => entry.Task.Id == pair.Key).OriginalStart != pair.Value)
            || !request.Plan.Entries.Select(entry => entry.Task.Id)
                .SequenceEqual(result.RevisedOrder);
        return CreateProposal(
            request,
            active,
            result,
            changed ? ScheduleRepairStatus.RequiresApproval : ScheduleRepairStatus.NoChange,
            ScheduleRepairIssue.None,
            deferral,
            protectedFixed,
            shutdown);
    }

    private static ScheduleRepairIssue ValidateProtectedSchedule(
        IReadOnlyList<EntryState> entries,
        TaskId? currentTaskId,
        TimeSpan now,
        TimeSpan shutdown,
        TimeSpan currentRemaining)
    {
        if (shutdown <= now)
        {
            return ScheduleRepairIssue.ShutdownHasPassed;
        }

        if (entries.Any(entry => entry.OriginalEnd > DayLength))
        {
            return ScheduleRepairIssue.ScheduleCrossesMidnight;
        }

        EntryState[] fixedEntries = entries
            .Where(entry => entry.Task.ScheduleType == ScheduleType.Fixed)
            .OrderBy(entry => entry.OriginalStart)
            .ThenBy(entry => entry.OriginalPosition)
            .ToArray();
        for (int index = 1; index < fixedEntries.Length; index++)
        {
            if (fixedEntries[index].OriginalStart < fixedEntries[index - 1].OriginalEnd)
            {
                return ScheduleRepairIssue.FixedCommitmentsOverlap;
            }
        }

        if (fixedEntries.Any(entry => entry.OriginalEnd > shutdown))
        {
            return ScheduleRepairIssue.FixedCommitmentExceedsShutdown;
        }

        if (fixedEntries.Any(entry =>
                entry.Task.Id != currentTaskId
                && entry.OriginalStart < now))
        {
            return ScheduleRepairIssue.FixedCommitmentMissed;
        }

        if (currentTaskId is not null && currentRemaining > TimeSpan.Zero)
        {
            TimeSpan currentEnd = Add(now, currentRemaining);
            if (currentEnd > DayLength)
            {
                return ScheduleRepairIssue.ScheduleCrossesMidnight;
            }

            if (fixedEntries.Any(entry =>
                    entry.Task.Id != currentTaskId
                    && Intersects(now, currentEnd, entry.OriginalStart, entry.OriginalEnd)))
            {
                return ScheduleRepairIssue.CurrentSessionOverlapsFixed;
            }
        }

        return ScheduleRepairIssue.None;
    }

    private static PlacementResult PlaceFlexibleWork(
        ScheduleRepairRequest request,
        IReadOnlyList<EntryState> active,
        TaskId? deferredTaskId)
    {
        TimeSpan now = request.CurrentTime.ToTimeSpan();
        TimeSpan cursor = request.CurrentTaskId is null
            ? now
            : Add(now, request.CurrentTaskRemaining);
        EntryState[] fixedEntries = active
            .Where(entry => entry.Task.ScheduleType == ScheduleType.Fixed)
            .OrderBy(entry => entry.OriginalStart)
            .ThenBy(entry => entry.OriginalPosition)
            .ToArray();
        EntryState[] flexibleEntries = active
            .Where(entry => entry.Task.ScheduleType == ScheduleType.Flexible)
            .Where(entry => entry.Task.Id != request.CurrentTaskId)
            .Where(entry => entry.Task.Id != deferredTaskId)
            .OrderBy(entry => entry.OriginalPosition)
            .ToArray();
        var starts = new Dictionary<TaskId, TimeSpan>();

        foreach (EntryState flexible in flexibleEntries)
        {
            TimeSpan start = Max(flexible.OriginalStart, cursor, now);
            bool movedPastFixed;
            do
            {
                movedPastFixed = false;
                foreach (EntryState fixedEntry in fixedEntries)
                {
                    TimeSpan end = Add(start, flexible.Duration);
                    if (Intersects(start, end, fixedEntry.OriginalStart, fixedEntry.OriginalEnd))
                    {
                        start = fixedEntry.OriginalEnd;
                        movedPastFixed = true;
                    }
                }
            }
            while (movedPastFixed);

            starts.Add(flexible.Task.Id, start);
            cursor = Add(start, flexible.Duration);
        }

        foreach (EntryState fixedEntry in fixedEntries)
        {
            starts[fixedEntry.Task.Id] = fixedEntry.OriginalStart;
        }

        if (request.CurrentTaskId is { } currentTaskId)
        {
            EntryState current = active.Single(entry => entry.Task.Id == currentTaskId);
            starts[currentTaskId] = current.OriginalStart;
        }

        EntryState[] retained = active
            .Where(entry => entry.Task.Id != deferredTaskId)
            .ToArray();
        TimeSpan finish = retained.Length == 0
            ? now
            : retained.Max(entry => entry.Task.Id == request.CurrentTaskId
                ? Add(now, request.CurrentTaskRemaining)
                : Add(starts[entry.Task.Id], entry.Duration));
        Dictionary<TaskId, TimeSpan> allStarts = request.Plan.Entries.ToDictionary(
            entry => entry.Task.Id,
            entry => starts.TryGetValue(entry.Task.Id, out TimeSpan revised)
                ? revised
                : entry.Task.PlannedStart.ToTimeSpan());
        TaskId[] order = request.Plan.Entries
            .OrderBy(entry => allStarts[entry.Task.Id])
            .ThenBy(entry => entry.Position)
            .Select(entry => entry.Task.Id)
            .ToArray();
        IReadOnlyList<BufferConsumption> buffer = CalculateConsumedBuffer(
            request,
            retained,
            starts,
            finish);
        return new PlacementResult(starts, order, finish, buffer);
    }

    private static List<BufferConsumption> CalculateConsumedBuffer(
        ScheduleRepairRequest request,
        IReadOnlyList<EntryState> retained,
        Dictionary<TaskId, TimeSpan> revisedStarts,
        TimeSpan revisedFinish)
    {
        EntryState[] originalOrder = retained
            .OrderBy(entry => entry.OriginalStart)
            .ThenBy(entry => entry.OriginalPosition)
            .ToArray();
        EntryState[] revisedOrder = retained
            .OrderBy(entry => revisedStarts[entry.Task.Id])
            .ThenBy(entry => entry.OriginalPosition)
            .ToArray();
        var originalGaps = CalculateGaps(
            originalOrder,
            entry => entry.OriginalStart,
            entry => entry.OriginalEnd,
            request.ShutdownTime.ToTimeSpan());
        var revisedGaps = CalculateGaps(
            revisedOrder,
            entry => revisedStarts[entry.Task.Id],
            entry => entry.Task.Id == request.CurrentTaskId
                ? Add(request.CurrentTime.ToTimeSpan(), request.CurrentTaskRemaining)
                : Add(revisedStarts[entry.Task.Id], entry.Duration),
            request.ShutdownTime.ToTimeSpan());
        var consumed = new List<BufferConsumption>();
        foreach ((GapBoundary boundary, TimeSpan originalGap) in originalGaps)
        {
            revisedGaps.TryGetValue(boundary, out TimeSpan revisedGap);
            TimeSpan amount = originalGap - revisedGap;
            if (amount > TimeSpan.Zero)
            {
                consumed.Add(new BufferConsumption(boundary.BeforeTaskId, amount));
            }
        }

        _ = revisedFinish;
        return consumed;
    }

    private static Dictionary<GapBoundary, TimeSpan> CalculateGaps(
        IReadOnlyList<EntryState> entries,
        Func<EntryState, TimeSpan> getStart,
        Func<EntryState, TimeSpan> getEnd,
        TimeSpan shutdown)
    {
        var result = new Dictionary<GapBoundary, TimeSpan>();
        TimeSpan? previousEnd = null;
        foreach (EntryState entry in entries)
        {
            TimeSpan start = getStart(entry);
            if (previousEnd is not null && start > previousEnd.Value)
            {
                result[new GapBoundary(entry.Task.Id)] = start - previousEnd.Value;
            }

            TimeSpan end = getEnd(entry);
            previousEnd = previousEnd is null || end > previousEnd ? end : previousEnd;
        }

        if (previousEnd is not null && shutdown > previousEnd.Value)
        {
            result[new GapBoundary(null)] = shutdown - previousEnd.Value;
        }

        return result;
    }

    private static EntryState? SelectDeferralCandidate(
        IReadOnlyList<EntryState> active,
        TaskId? currentTaskId)
    {
        return active
            .Where(entry => entry.Task.ScheduleType == ScheduleType.Flexible)
            .Where(entry => entry.Task.Id != currentTaskId)
            .OrderBy(entry => entry.Task.Importance == TaskImportance.Normal ? 0 : 1)
            .ThenByDescending(entry => entry.OriginalPosition)
            .FirstOrDefault();
    }

    private static ScheduleRepairProposal CreateImpossible(
        ScheduleRepairRequest request,
        IReadOnlyList<EntryState> active,
        IReadOnlyList<ProtectedFixedCommitment> protectedFixed,
        ScheduleRepairIssue issue,
        TimeSpan shutdown)
    {
        PlacementResult unchanged = new(
            active.ToDictionary(entry => entry.Task.Id, entry => entry.OriginalStart),
            request.Plan.Entries.Select(entry => entry.Task.Id).ToArray(),
            active.Count == 0 ? request.CurrentTime.ToTimeSpan() : active.Max(entry => entry.OriginalEnd),
            []);
        return CreateProposal(
            request,
            active,
            unchanged,
            ScheduleRepairStatus.Impossible,
            issue,
            deferral: null,
            protectedFixed,
            shutdown);
    }

    private static ScheduleRepairProposal CreateProposal(
        ScheduleRepairRequest request,
        IReadOnlyList<EntryState> active,
        PlacementResult placement,
        ScheduleRepairStatus status,
        ScheduleRepairIssue issue,
        ScheduleDeferral? deferral,
        IReadOnlyList<ProtectedFixedCommitment> protectedFixed,
        TimeSpan shutdown)
    {
        Dictionary<TaskId, int> revisedPositions = placement.RevisedOrder
            .Select((taskId, position) => (taskId, position))
            .ToDictionary(item => item.taskId, item => item.position);
        var moves = new List<ScheduleMove>();
        foreach (EntryState entry in active.Where(entry =>
                     entry.Task.ScheduleType == ScheduleType.Flexible
                     && entry.Task.Id != deferral?.TaskId))
        {
            TimeSpan revisedStart = placement.RevisedStarts[entry.Task.Id];
            int revisedPosition = revisedPositions[entry.Task.Id];
            if (revisedStart != entry.OriginalStart
                || revisedPosition != entry.OriginalPosition)
            {
                moves.Add(new ScheduleMove(
                    entry.Task.Id,
                    TimeOnly.FromTimeSpan(entry.OriginalStart),
                    TimeOnly.FromTimeSpan(revisedStart),
                    entry.OriginalPosition,
                    revisedPosition));
            }
        }

        TimeSpan overflow = placement.RevisedFinish > shutdown
            ? placement.RevisedFinish - shutdown
            : TimeSpan.Zero;
        return new ScheduleRepairProposal(
            request,
            status,
            issue,
            placement.BufferConsumptions,
            moves,
            deferral,
            placement.RevisedFinish,
            overflow,
            protectedFixed,
            placement.RevisedOrder);
    }

    private static bool Intersects(
        TimeSpan firstStart,
        TimeSpan firstEnd,
        TimeSpan secondStart,
        TimeSpan secondEnd)
    {
        return firstStart < secondEnd && secondStart < firstEnd;
    }

    private static TimeSpan Add(TimeSpan value, TimeSpan duration)
    {
        try
        {
            return value + duration;
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("Schedule duration is too large.", nameof(duration), exception);
        }
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second, TimeSpan third)
    {
        return first > second
            ? first > third ? first : third
            : second > third ? second : third;
    }

    private sealed record EntryState(
        DomainTask Task,
        int OriginalPosition,
        TimeSpan OriginalStart,
        TimeSpan Duration)
    {
        internal TimeSpan OriginalEnd => Add(OriginalStart, Duration);
    }

    private sealed record PlacementResult(
        IReadOnlyDictionary<TaskId, TimeSpan> RevisedStarts,
        IReadOnlyList<TaskId> RevisedOrder,
        TimeSpan RevisedFinish,
        IReadOnlyList<BufferConsumption> BufferConsumptions);

    private readonly record struct GapBoundary(TaskId? BeforeTaskId);
}
