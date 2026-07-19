using NowNext.Core.Domain;
using NowNext.Core.Sessions;

namespace NowNext.Core.Tests.Sessions;

[TestClass]
public sealed class ContextCapsuleTests
{
    private static readonly TaskId TaskId = new(Guid.Parse(
        "72279e21-adf7-4d69-9e24-48678632c25e"));
    private static readonly SessionId SessionId = new(Guid.Parse(
        "15020572-bbdc-4e3b-acf2-28de0d7ed1ba"));

    [TestMethod]
    public void CapsuleTrimsRequiredActionAndOptionalNoteAndNormalizesTimestamp()
    {
        var capsule = new ContextCapsule(
            TaskId,
            SessionId,
            "  Open the review comments  ",
            "  Begin with the unresolved question.  ",
            new DateTimeOffset(2026, 7, 18, 10, 30, 0, TimeSpan.FromHours(-4)));

        Assert.AreEqual("Open the review comments", capsule.NextPhysicalAction);
        Assert.AreEqual("Begin with the unresolved question.", capsule.Note);
        Assert.AreEqual(TimeSpan.Zero, capsule.SavedAtUtc.Offset);
    }

    [TestMethod]
    public void CapsuleTreatsBlankNoteAsAbsent()
    {
        var capsule = new ContextCapsule(
            TaskId,
            SessionId,
            "Open the review comments",
            "  ",
            DateTimeOffset.UtcNow);

        Assert.IsNull(capsule.Note);
    }

    [TestMethod]
    public void CapsuleRejectsMissingNextAction()
    {
        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => new ContextCapsule(
                TaskId,
                SessionId,
                " ",
                null,
                DateTimeOffset.UtcNow));

        Assert.Contains("next physical action", exception.Message);
    }
}
