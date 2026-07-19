using NowNext.Core.Domain;

namespace NowNext.Core.Sessions;

public sealed record ContextCapsule
{
    public ContextCapsule(
        TaskId taskId,
        SessionId sessionId,
        string nextPhysicalAction,
        string? note,
        DateTimeOffset savedAtUtc)
    {
        if (taskId.Value == Guid.Empty)
        {
            throw new ArgumentException("Task ID must not be empty.", nameof(taskId));
        }

        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));
        }

        TaskId = taskId;
        SessionId = sessionId;
        NextPhysicalAction = string.IsNullOrWhiteSpace(nextPhysicalAction)
            ? throw new ArgumentException(
                "A Context Capsule requires a next physical action.",
                nameof(nextPhysicalAction))
            : nextPhysicalAction.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        SavedAtUtc = savedAtUtc.ToUniversalTime();
    }

    public TaskId TaskId { get; }

    public SessionId SessionId { get; }

    public string NextPhysicalAction { get; }

    public string? Note { get; }

    public DateTimeOffset SavedAtUtc { get; }
}
