using NowNext.Core.Domain;
using DomainTask = NowNext.Core.Domain.Task;

namespace NowNext.Core.Tests.Domain;

[TestClass]
public sealed class TaskTests
{
    [TestMethod]
    public void ConstructorValidValuesPreservesAndTrimsFields()
    {
        TaskId taskId = new(Guid.NewGuid());

        var task = new DomainTask(
            taskId,
            "  Full title  ",
            "  Focus label  ",
            "  Definition  ",
            "  First action  ",
            "  Next action  ",
            new TimeOnly(14, 30),
            TimeSpan.FromMinutes(25),
            TimingMode.Countdown,
            ScheduleType.Flexible,
            TaskImportance.Important,
            TaskState.Parked);

        Assert.AreEqual(taskId, task.Id);
        Assert.AreEqual("Full title", task.FullTitle);
        Assert.AreEqual("Focus label", task.ShortFocusLabel);
        Assert.AreEqual("Definition", task.DefinitionOfDone);
        Assert.AreEqual("First action", task.FirstPhysicalAction);
        Assert.AreEqual("Next action", task.NextPhysicalAction);
        Assert.AreEqual(new TimeOnly(14, 30), task.PlannedStart);
        Assert.AreEqual(TimeSpan.FromMinutes(25), task.PlannedDuration);
        Assert.AreEqual(TimingMode.Countdown, task.TimingMode);
        Assert.AreEqual(ScheduleType.Flexible, task.ScheduleType);
        Assert.AreEqual(TaskImportance.Important, task.Importance);
        Assert.AreEqual(TaskState.Parked, task.State);
    }

    [TestMethod]
    [DataRow(TimingMode.CountUp, ScheduleType.Fixed)]
    [DataRow(TimingMode.CountUp, ScheduleType.Flexible)]
    [DataRow(TimingMode.Countdown, ScheduleType.Fixed)]
    [DataRow(TimingMode.Countdown, ScheduleType.Flexible)]
    public void ConstructorSupportedModesAcceptsEveryCombination(
        TimingMode timingMode,
        ScheduleType scheduleType)
    {
        DomainTask task = TestTaskFactory.Create(
            timingMode: timingMode,
            scheduleType: scheduleType);

        Assert.AreEqual(timingMode, task.TimingMode);
        Assert.AreEqual(scheduleType, task.ScheduleType);
    }

    [TestMethod]
    public void TaskIdEmptyValueThrowsUnderstandableFailure()
    {
        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => new TaskId(Guid.Empty));

        Assert.Contains("must not be empty", exception.Message);
    }

    [TestMethod]
    public void ConstructorBlankRequiredTextThrowsForEachField()
    {
        Action[] invalidConstructions =
        [
            () => TestTaskFactory.Create(fullTitle: " "),
            () => TestTaskFactory.Create(shortFocusLabel: " "),
            () => TestTaskFactory.Create(definitionOfDone: " "),
            () => TestTaskFactory.Create(firstPhysicalAction: " "),
            () => TestTaskFactory.Create(nextPhysicalAction: " "),
        ];

        foreach (Action construction in invalidConstructions)
        {
            ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(construction);
            Assert.Contains("blank", exception.Message);
        }
    }

    [TestMethod]
    public void ConstructorNonPositiveDurationThrowsUnderstandableFailure()
    {
        ArgumentException zeroException = Assert.ThrowsExactly<ArgumentException>(
            () => TestTaskFactory.Create(plannedDuration: TimeSpan.Zero));
        ArgumentException negativeException = Assert.ThrowsExactly<ArgumentException>(
            () => TestTaskFactory.Create(plannedDuration: TimeSpan.FromTicks(-1)));

        Assert.Contains("positive", zeroException.Message);
        Assert.Contains("positive", negativeException.Message);
    }

    [TestMethod]
    public void ConstructorUndefinedEnumsThrowUnderstandableFailure()
    {
        Action[] invalidConstructions =
        [
            () => TestTaskFactory.Create(timingMode: (TimingMode)99),
            () => TestTaskFactory.Create(scheduleType: (ScheduleType)99),
            () => TestTaskFactory.Create(importance: (TaskImportance)99),
            () => TestTaskFactory.Create(state: (TaskState)99),
        ];

        foreach (Action construction in invalidConstructions)
        {
            ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(construction);
            Assert.Contains("not defined", exception.Message);
        }
    }

    [TestMethod]
    public void ConstructorParkedWithoutNextActionThrowsUnderstandableFailure()
    {
        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => TestTaskFactory.Create(state: TaskState.Parked));

        Assert.Contains("parked task", exception.Message);
    }
}
