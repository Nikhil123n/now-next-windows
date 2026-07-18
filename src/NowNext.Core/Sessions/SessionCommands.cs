namespace NowNext.Core.Sessions;

public abstract record SessionCommand
{
    private protected SessionCommand()
    {
    }
}

public sealed record StartSession : SessionCommand;

public sealed record RefreshSession : SessionCommand;

public sealed record PauseSession : SessionCommand;

public sealed record ResumeSession : SessionCommand;

public sealed record ContinueOvertime : SessionCommand;

public sealed record BeginLanding : SessionCommand;

public sealed record ExtendSession : SessionCommand
{
    public ExtendSession(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Extension duration must be positive.", nameof(duration));
        }

        Duration = duration;
    }

    public TimeSpan Duration { get; }
}

public sealed record CompleteSession : SessionCommand;

public sealed record ParkSession : SessionCommand
{
    public ParkSession(string nextPhysicalAction)
    {
        NextPhysicalAction = string.IsNullOrWhiteSpace(nextPhysicalAction)
            ? throw new ArgumentException(
                "Parking requires a next physical action.",
                nameof(nextPhysicalAction))
            : nextPhysicalAction.Trim();
    }

    public string NextPhysicalAction { get; }
}

public sealed record BeginBreak : SessionCommand;

public sealed record EndBreak : SessionCommand;

public sealed record InterruptSession : SessionCommand;

public sealed record ResumeWithoutAwayTime : SessionCommand;

public sealed record ResumeIncludingAwayTime : SessionCommand
{
    public ResumeIncludingAwayTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentException("Included away time must not be negative.", nameof(duration));
        }

        Duration = duration;
    }

    public TimeSpan Duration { get; }
}

public sealed record CloseDay : SessionCommand;
