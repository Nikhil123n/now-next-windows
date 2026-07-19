using NowNext.Core.Domain;
using NowNext.Core.Planning;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Planning;

[TestClass]
public sealed class WorkdayProjectionTests
{
    [TestMethod]
    public void CreateRecoveryOverviewUsesEarlierNextFixedCommitment()
    {
        DomainTask interrupted = TaskAt(9, 0, ScheduleType.Flexible);
        DomainTask nextFixed = TaskAt(11, 0, ScheduleType.Fixed);
        DomainTask laterFixed = TaskAt(15, 0, ScheduleType.Fixed);
        TodayPlan plan = Plan(interrupted, nextFixed, laterFixed);

        RecoveryOverview overview = WorkdayProjections.CreateRecoveryOverview(
            plan,
            interrupted.Id,
            new TimeOnly(10, 15),
            new TimeOnly(17, 0));

        Assert.AreEqual(nextFixed.Id, overview.NextFixedTaskId);
        Assert.AreEqual(TimeSpan.FromMinutes(45), overview.RealisticAvailableTime);
    }

    [TestMethod]
    public void CreateRecoveryOverviewLateReturnNeverReportsNegativeTime()
    {
        DomainTask interrupted = TaskAt(9, 0, ScheduleType.Flexible);
        DomainTask fixedTask = TaskAt(10, 0, ScheduleType.Fixed, durationMinutes: 120);

        RecoveryOverview overview = WorkdayProjections.CreateRecoveryOverview(
            Plan(interrupted, fixedTask),
            interrupted.Id,
            new TimeOnly(11, 0),
            new TimeOnly(17, 0));

        Assert.AreEqual(fixedTask.Id, overview.NextFixedTaskId);
        Assert.AreEqual(TimeSpan.Zero, overview.RealisticAvailableTime);
    }

    [TestMethod]
    public void CreateShutdownSummaryReportsOutcomesDailyWinAndImportantNextAction()
    {
        DomainTask completed = TaskAt(
            9,
            0,
            ScheduleType.Flexible,
            state: TaskState.Completed);
        DomainTask deferred = TaskAt(
            10,
            0,
            ScheduleType.Flexible,
            state: TaskState.Deferred);
        DomainTask important = TestTaskFactory.Create(
            plannedStart: new TimeOnly(11, 0),
            scheduleType: ScheduleType.Flexible,
            importance: TaskImportance.Important,
            nextPhysicalAction: "Open the next draft");
        DomainTask normal = TaskAt(8, 0, ScheduleType.Flexible);
        TodayPlan plan = Plan(completed, deferred, normal, important);
        var actuals = new[]
        {
            new SessionActual(completed.Id, TimeSpan.FromMinutes(35)),
            new SessionActual(completed.Id, TimeSpan.FromMinutes(10)),
            new SessionActual(deferred.Id, TimeSpan.FromMinutes(5)),
        };
        var capsules = new Dictionary<TaskId, string>
        {
            [important.Id] = "  Review the marked paragraph  ",
        };

        ShutdownSummary summary = WorkdayProjections.CreateShutdownSummary(
            plan,
            actuals,
            completed.Id,
            capsules);

        Assert.HasCount(1, summary.Completed);
        Assert.HasCount(1, summary.Deferred);
        Assert.AreEqual(DailyWinStatus.Completed, summary.DailyWinStatus);
        Assert.AreEqual(important.Id, summary.NextUnfinishedTaskId);
        Assert.AreEqual("Review the marked paragraph", summary.NextPhysicalAction);
        Assert.AreEqual(TimeSpan.FromMinutes(50), summary.TotalActualDuration);
    }

    [TestMethod]
    public void CreateShutdownSummaryWithoutCapsuleFallsBackToFirstAction()
    {
        DomainTask unfinished = TestTaskFactory.Create(
            nextPhysicalAction: null,
            firstPhysicalAction: "Open the source file",
            scheduleType: ScheduleType.Flexible);

        ShutdownSummary summary = WorkdayProjections.CreateShutdownSummary(
            Plan(unfinished),
            [],
            dailyWinTaskId: null);

        Assert.AreEqual(DailyWinStatus.NotSelected, summary.DailyWinStatus);
        Assert.AreEqual("Open the source file", summary.NextPhysicalAction);
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
        ScheduleType scheduleType,
        TaskImportance importance = TaskImportance.Normal,
        TaskState state = TaskState.Planned,
        double durationMinutes = 30)
    {
        return TestTaskFactory.Create(
            plannedStart: new TimeOnly(hour, minute),
            plannedDuration: TimeSpan.FromMinutes(durationMinutes),
            scheduleType: scheduleType,
            importance: importance,
            state: state);
    }
}
