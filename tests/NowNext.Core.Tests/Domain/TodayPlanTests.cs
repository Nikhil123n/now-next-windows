using NowNext.Core.Domain;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Domain;

[TestClass]
public sealed class TodayPlanTests
{
    [TestMethod]
    public void ConstructorDuplicateTaskIdsThrowsUnderstandableFailure()
    {
        DomainTask task = TestTaskFactory.Create();
        ScheduleEntry[] entries =
        [
            new(task, 0),
            new(task, 1),
        ];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => new TodayPlan(new DateOnly(2026, 7, 18), entries));

        Assert.Contains(task.Id.ToString(), exception.Message);
    }

    [TestMethod]
    public void ConstructorNonContiguousPositionsThrowsUnderstandableFailure()
    {
        ScheduleEntry[] entries =
        [
            new(TestTaskFactory.Create(), 0),
            new(TestTaskFactory.Create(), 2),
        ];

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => new TodayPlan(new DateOnly(2026, 7, 18), entries));

        Assert.Contains("contiguous", exception.Message);
    }

    [TestMethod]
    public void ReorderCompleteOrderReturnsNewContiguousPlan()
    {
        DomainTask first = TestTaskFactory.Create();
        DomainTask second = TestTaskFactory.Create();
        var original = new TodayPlan(
            new DateOnly(2026, 7, 18),
            [new ScheduleEntry(first, 0), new ScheduleEntry(second, 1)]);

        TodayPlan reordered = original.Reorder([second.Id, first.Id]);

        Assert.AreEqual(second.Id, reordered.Entries[0].Task.Id);
        Assert.AreEqual(0, reordered.Entries[0].Position);
        Assert.AreEqual(first.Id, reordered.Entries[1].Task.Id);
        Assert.AreEqual(1, reordered.Entries[1].Position);
        Assert.AreEqual(first.Id, original.Entries[0].Task.Id);
    }

    [TestMethod]
    public void ReorderMissingTaskThrowsUnderstandableFailure()
    {
        TodayPlan plan = CreateTwoTaskPlan(out DomainTask first, out _);

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => plan.Reorder([first.Id]));

        Assert.Contains("every current task", exception.Message);
    }

    [TestMethod]
    public void ReorderDuplicateTaskThrowsUnderstandableFailure()
    {
        TodayPlan plan = CreateTwoTaskPlan(out DomainTask first, out _);

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => plan.Reorder([first.Id, first.Id]));

        Assert.Contains("duplicate", exception.Message);
    }

    [TestMethod]
    public void ReorderUnknownTaskThrowsUnderstandableFailure()
    {
        TodayPlan plan = CreateTwoTaskPlan(out DomainTask first, out _);
        TaskId unknown = new(Guid.NewGuid());

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => plan.Reorder([first.Id, unknown]));

        Assert.Contains(unknown.ToString(), exception.Message);
    }

    private static TodayPlan CreateTwoTaskPlan(out DomainTask first, out DomainTask second)
    {
        first = TestTaskFactory.Create();
        second = TestTaskFactory.Create();
        return new TodayPlan(
            new DateOnly(2026, 7, 18),
            [new ScheduleEntry(first, 0), new ScheduleEntry(second, 1)]);
    }
}
