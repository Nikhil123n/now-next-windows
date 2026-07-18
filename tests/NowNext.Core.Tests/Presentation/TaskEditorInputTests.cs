using NowNext.App.Presentation;
using NowNext.Core.Domain;

namespace NowNext.Core.Tests.Presentation;

[TestClass]
public sealed class TaskEditorInputTests
{
    [TestMethod]
    public void TryCreateTaskValidApprovedFieldsBuildsTrimmedDomainTask()
    {
        var input = new TaskEditorInput(
            "  Write test plan  ",
            "  Test plan  ",
            "  Manual cases are documented  ",
            "  Open the test guide  ",
            TimeSpan.FromHours(9),
            25,
            TimingMode.Countdown,
            ScheduleType.Fixed,
            TaskImportance.Important);

        bool created = input.TryCreateTask(
            new TaskId(Guid.NewGuid()),
            null,
            TaskState.Planned,
            out NowNext.Core.Domain.Task? task,
            out string error);

        Assert.IsTrue(created);
        Assert.IsNotNull(task);
        Assert.AreEqual(string.Empty, error);
        Assert.AreEqual("Write test plan", task.FullTitle);
        Assert.AreEqual("Test plan", task.ShortFocusLabel);
        Assert.AreEqual(new TimeOnly(9, 0), task.PlannedStart);
        Assert.AreEqual(TimeSpan.FromMinutes(25), task.PlannedDuration);
        Assert.AreEqual(TimingMode.Countdown, task.TimingMode);
        Assert.AreEqual(ScheduleType.Fixed, task.ScheduleType);
        Assert.AreEqual(TaskImportance.Important, task.Importance);
    }

    [DataRow("", "Focus", "Enter a full task title.")]
    [DataRow("Title", "  ", "Enter the short focus label shown during focus.")]
    [TestMethod]
    public void TryCreateTaskBlankVisibleIdentityReturnsSpecificMessage(
        string title,
        string focusLabel,
        string expectedMessage)
    {
        TaskEditorInput input = CreateValidInput(title, focusLabel);

        bool created = input.TryCreateTask(
            new TaskId(Guid.NewGuid()),
            null,
            TaskState.Planned,
            out NowNext.Core.Domain.Task? task,
            out string error);

        Assert.IsFalse(created);
        Assert.IsNull(task);
        Assert.AreEqual(expectedMessage, error);
    }

    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(double.NaN)]
    [TestMethod]
    public void TryCreateTaskInvalidDurationReturnsUnderstandableMessage(double minutes)
    {
        TaskEditorInput input = CreateValidInput() with { PlannedDurationMinutes = minutes };

        bool created = input.TryCreateTask(
            new TaskId(Guid.NewGuid()),
            null,
            TaskState.Planned,
            out NowNext.Core.Domain.Task? task,
            out string error);

        Assert.IsFalse(created);
        Assert.IsNull(task);
        Assert.AreEqual("Enter a planned duration greater than zero minutes.", error);
    }

    [TestMethod]
    public void TryCreateTaskMissingPlannedTimeReturnsUnderstandableMessage()
    {
        TaskEditorInput input = CreateValidInput() with { PlannedStart = null };

        bool created = input.TryCreateTask(
            new TaskId(Guid.NewGuid()),
            null,
            TaskState.Planned,
            out NowNext.Core.Domain.Task? task,
            out string error);

        Assert.IsFalse(created);
        Assert.IsNull(task);
        Assert.AreEqual("Choose a valid planned start time.", error);
    }

    private static TaskEditorInput CreateValidInput(
        string title = "Write test plan",
        string focusLabel = "Test plan")
    {
        return new TaskEditorInput(
            title,
            focusLabel,
            "Manual cases are documented",
            "Open the test guide",
            TimeSpan.FromHours(9),
            25,
            TimingMode.CountUp,
            ScheduleType.Flexible,
            TaskImportance.Normal);
    }
}
