using NowNext.Core.Domain;
using NowNext.Core.Planning;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Planning;

[TestClass]
public sealed class ScheduleRepairEngineTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 18, 14, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ProposeExtensionFitsInBufferReportsConsumptionWithoutChanges()
    {
        DomainTask current = TaskAt(9, 0, 60, ScheduleType.Flexible);
        DomainTask later = TaskAt(10, 30, 30, ScheduleType.Flexible);
        DomainTask fixedTask = TaskAt(12, 0, 30, ScheduleType.Fixed);
        TodayPlan plan = Plan(current, later, fixedTask);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            plan,
            new TimeOnly(9, 45),
            new TimeOnly(17, 0),
            current.Id,
            TimeSpan.FromMinutes(30)));

        Assert.AreEqual(ScheduleRepairStatus.NoChange, proposal.Status);
        Assert.AreEqual(TimeSpan.FromMinutes(15), proposal.BufferConsumed);
        BufferConsumption consumption = Assert.ContainsSingle(proposal.BufferConsumptions);
        Assert.AreEqual(later.Id, consumption.BeforeTaskId);
        Assert.AreEqual(TimeSpan.FromMinutes(15), consumption.Duration);
        Assert.IsEmpty(proposal.Moves);
        Assert.IsNull(proposal.Deferral);
        Assert.HasCount(1, proposal.ProtectedFixedCommitments);
    }

    [TestMethod]
    public void ProposeZeroBufferShiftsFlexibleWorkPastFixedCommitment()
    {
        DomainTask current = TaskAt(9, 0, 60, ScheduleType.Flexible);
        DomainTask movable = TaskAt(10, 0, 45, ScheduleType.Flexible);
        DomainTask fixedTask = TaskAt(10, 30, 30, ScheduleType.Fixed);
        TodayPlan plan = Plan(current, movable, fixedTask);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            plan,
            new TimeOnly(9, 50),
            new TimeOnly(17, 0),
            current.Id,
            TimeSpan.FromMinutes(20)));

        Assert.AreEqual(ScheduleRepairStatus.RequiresApproval, proposal.Status);
        ScheduleMove move = Assert.ContainsSingle(proposal.Moves);
        Assert.AreEqual(movable.Id, move.TaskId);
        Assert.AreEqual(new TimeOnly(11, 0), move.RevisedStart);
        Assert.AreEqual(fixedTask.Id, proposal.RevisedTaskOrder[1]);
        Assert.AreEqual(movable.Id, proposal.RevisedTaskOrder[2]);
        Assert.AreEqual(new TimeOnly(10, 30), proposal.ProtectedFixedCommitments[0].Start);
    }

    [TestMethod]
    public void ProposeOverflowSelectsLatestNormalFlexibleTask()
    {
        DomainTask first = TaskAt(13, 0, 90, ScheduleType.Flexible);
        DomainTask important = TaskAt(
            14,
            30,
            90,
            ScheduleType.Flexible,
            TaskImportance.Important);
        DomainTask latestNormal = TaskAt(16, 0, 60, ScheduleType.Flexible);
        TodayPlan plan = Plan(first, important, latestNormal);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            plan,
            new TimeOnly(13, 0),
            new TimeOnly(16, 30)));

        Assert.AreEqual(ScheduleRepairStatus.RequiresApproval, proposal.Status);
        Assert.AreEqual(latestNormal.Id, proposal.Deferral?.TaskId);
        Assert.AreEqual(TimeSpan.Zero, proposal.Overflow);
    }

    [TestMethod]
    public void ProposeOnlyImportantFlexibleWorkSelectsLatestImportantTask()
    {
        DomainTask first = TaskAt(
            14,
            0,
            90,
            ScheduleType.Flexible,
            TaskImportance.Important);
        DomainTask latest = TaskAt(
            15,
            30,
            90,
            ScheduleType.Flexible,
            TaskImportance.Important);
        TodayPlan plan = Plan(first, latest);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            plan,
            new TimeOnly(14, 0),
            new TimeOnly(16, 0)));

        Assert.AreEqual(latest.Id, proposal.Deferral?.TaskId);
    }

    [TestMethod]
    public void ProposeSingleDeferralIsInsufficientReturnsImpossibleProposal()
    {
        DomainTask first = TaskAt(13, 0, 120, ScheduleType.Flexible);
        DomainTask latest = TaskAt(15, 0, 30, ScheduleType.Flexible);
        TodayPlan plan = Plan(first, latest);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            plan,
            new TimeOnly(15, 0),
            new TimeOnly(15, 30)));

        Assert.AreEqual(ScheduleRepairStatus.Impossible, proposal.Status);
        Assert.AreEqual(ScheduleRepairIssue.InsufficientFlexibleTime, proposal.Issue);
        Assert.IsFalse(proposal.CanApply);
        Assert.AreEqual(latest.Id, proposal.Deferral?.TaskId);
        Assert.IsGreaterThan(TimeSpan.Zero, proposal.Overflow);
    }

    [TestMethod]
    public void ProposeOverlappingFixedTasksProtectsBothAndRejectsApply()
    {
        DomainTask first = TaskAt(10, 0, 60, ScheduleType.Fixed);
        DomainTask second = TaskAt(10, 30, 60, ScheduleType.Fixed);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            Plan(first, second),
            new TimeOnly(9, 0),
            new TimeOnly(17, 0)));

        Assert.AreEqual(ScheduleRepairIssue.FixedCommitmentsOverlap, proposal.Issue);
        Assert.IsFalse(proposal.CanApply);
        Assert.HasCount(2, proposal.ProtectedFixedCommitments);
    }

    [TestMethod]
    public void ProposeCurrentSessionWouldOverlapFixedRejectsApply()
    {
        DomainTask current = TaskAt(9, 0, 60, ScheduleType.Flexible);
        DomainTask fixedTask = TaskAt(10, 0, 30, ScheduleType.Fixed);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            Plan(current, fixedTask),
            new TimeOnly(9, 50),
            new TimeOnly(17, 0),
            current.Id,
            TimeSpan.FromMinutes(20)));

        Assert.AreEqual(ScheduleRepairIssue.CurrentSessionOverlapsFixed, proposal.Issue);
        Assert.AreEqual(new TimeOnly(10, 0), proposal.ProtectedFixedCommitments[0].Start);
    }

    [TestMethod]
    public void ProposeFixedExtendsPastShutdownRejectsApply()
    {
        DomainTask fixedTask = TaskAt(16, 30, 60, ScheduleType.Fixed);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            Plan(fixedTask),
            new TimeOnly(9, 0),
            new TimeOnly(17, 0)));

        Assert.AreEqual(ScheduleRepairIssue.FixedCommitmentExceedsShutdown, proposal.Issue);
        Assert.IsFalse(proposal.CanApply);
    }

    [TestMethod]
    public void ProposeAfterShutdownReturnsImpossibleLateReturn()
    {
        DomainTask flexible = TaskAt(16, 0, 30, ScheduleType.Flexible);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            Plan(flexible),
            new TimeOnly(17, 0),
            new TimeOnly(17, 0)));

        Assert.AreEqual(ScheduleRepairStatus.Impossible, proposal.Status);
        Assert.AreEqual(ScheduleRepairIssue.ShutdownHasPassed, proposal.Issue);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.Zero, proposal.Overflow);
    }

    [TestMethod]
    public void ProposeAfterMissedFixedCommitmentProtectsItWithoutMovingIt()
    {
        DomainTask fixedTask = TaskAt(10, 0, 30, ScheduleType.Fixed);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            Plan(fixedTask),
            new TimeOnly(10, 45),
            new TimeOnly(17, 0)));

        Assert.AreEqual(ScheduleRepairIssue.FixedCommitmentMissed, proposal.Issue);
        ProtectedFixedCommitment protectedTask =
            Assert.ContainsSingle(proposal.ProtectedFixedCommitments);
        Assert.AreEqual(new TimeOnly(10, 0), protectedTask.Start);
        Assert.IsEmpty(proposal.Moves);
    }

    [TestMethod]
    public void ProposeFlexibleWorkThatWouldReachMidnightReturnsImpossibleProposal()
    {
        DomainTask first = TaskAt(20, 0, 150, ScheduleType.Flexible);
        DomainTask second = TaskAt(22, 30, 60, ScheduleType.Flexible);

        ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
            Plan(first, second),
            new TimeOnly(22, 50),
            new TimeOnly(23, 59)));

        Assert.AreEqual(ScheduleRepairStatus.Impossible, proposal.Status);
        Assert.AreEqual(ScheduleRepairIssue.ScheduleCrossesMidnight, proposal.Issue);
        Assert.IsFalse(proposal.CanApply);
    }

    [TestMethod]
    public void ProposeSameRequestTwiceIsDeterministic()
    {
        DomainTask flexible = TaskAt(9, 0, 60, ScheduleType.Flexible);
        DomainTask fixedTask = TaskAt(10, 30, 30, ScheduleType.Fixed);
        ScheduleRepairRequest request = Request(
            Plan(flexible, fixedTask),
            new TimeOnly(9, 45),
            new TimeOnly(17, 0),
            flexible.Id,
            TimeSpan.FromMinutes(30));

        ScheduleRepairProposal first = ScheduleRepairEngine.Propose(request);
        ScheduleRepairProposal second = ScheduleRepairEngine.Propose(request);

        Assert.AreEqual(first.Status, second.Status);
        Assert.AreEqual(first.Issue, second.Issue);
        Assert.AreEqual(first.BufferConsumed, second.BufferConsumed);
        Assert.AreSequenceEqual(first.Moves, second.Moves);
        Assert.AreSequenceEqual(first.RevisedTaskOrder, second.RevisedTaskOrder);
    }

    [TestMethod]
    public void FixedSeedScheduleVariationsNeverMoveFixedCommitmentOrShutdown()
    {
        var random = new Random(7007);
        for (int sequence = 0; sequence < 64; sequence++)
        {
            DomainTask morning = TaskAt(
                9,
                0,
                random.Next(1, 4) * 15,
                ScheduleType.Flexible);
            DomainTask fixedTask = TaskAt(12, 0, 30, ScheduleType.Fixed);
            DomainTask afternoon = TaskAt(
                13,
                0,
                random.Next(1, 5) * 15,
                ScheduleType.Flexible);
            TimeOnly shutdown = new(17, 0);

            ScheduleRepairProposal proposal = ScheduleRepairEngine.Propose(Request(
                Plan(morning, fixedTask, afternoon),
                new TimeOnly(9, random.Next(0, 46)),
                shutdown));

            ProtectedFixedCommitment protection =
                Assert.ContainsSingle(proposal.ProtectedFixedCommitments);
            Assert.AreEqual(fixedTask.Id, protection.TaskId);
            Assert.AreEqual(fixedTask.PlannedStart, protection.Start);
            Assert.AreEqual(shutdown, proposal.Request.ShutdownTime);
            Assert.IsEmpty(proposal.Moves.Where(move => move.TaskId == fixedTask.Id));
        }
    }

    private static ScheduleRepairRequest Request(
        TodayPlan plan,
        TimeOnly now,
        TimeOnly shutdown,
        TaskId? currentTaskId = null,
        TimeSpan? remaining = null)
    {
        return new ScheduleRepairRequest(
            new ScheduleRepairId(Guid.Parse("F22C5359-242F-472E-8B6C-43E2E86143AC")),
            3,
            plan,
            now,
            shutdown,
            new ScheduleRepairTrigger(
                ScheduleRepairTriggerKind.CurrentTime,
                ObservedAt),
            currentTaskId,
            remaining);
    }

    private static TodayPlan Plan(params DomainTask[] tasks)
    {
        return new TodayPlan(
            new DateOnly(2026, 7, 18),
            tasks.Select((task, index) => new ScheduleEntry(task, index)).ToArray());
    }

    private static DomainTask TaskAt(
        int hour,
        int minute,
        double durationMinutes,
        ScheduleType scheduleType,
        TaskImportance importance = TaskImportance.Normal)
    {
        return TestTaskFactory.Create(
            plannedStart: new TimeOnly(hour, minute),
            plannedDuration: TimeSpan.FromMinutes(durationMinutes),
            scheduleType: scheduleType,
            importance: importance);
    }
}
