namespace NowNext.Core.Sessions;

public enum BreakPromptKind
{
    DistantGaze,
    Water,
    JawRelaxation,
    ShoulderRelease,
    Stand,
    Walk,
    UserSelectedMovement,
}

public sealed record BreakPrompt
{
    public BreakPrompt(BreakPromptKind kind, string? userSelectedMovement = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Break prompt kind is not defined.", nameof(kind));
        }

        if (kind == BreakPromptKind.UserSelectedMovement)
        {
            Text = string.IsNullOrWhiteSpace(userSelectedMovement)
                ? throw new ArgumentException(
                    "A user-selected movement prompt must not be blank.",
                    nameof(userSelectedMovement))
                : userSelectedMovement.Trim();
        }
        else
        {
            if (userSelectedMovement is not null)
            {
                throw new ArgumentException(
                    "A built-in Break prompt must not contain custom movement text.",
                    nameof(userSelectedMovement));
            }

            Text = kind switch
            {
                BreakPromptKind.DistantGaze => "Look at something distant.",
                BreakPromptKind.Water => "Have some water.",
                BreakPromptKind.JawRelaxation => "Let your jaw relax.",
                BreakPromptKind.ShoulderRelease => "Release your shoulders.",
                BreakPromptKind.Stand => "Stand for a moment.",
                BreakPromptKind.Walk => "Take a short walk.",
                _ => throw new InvalidOperationException("Break prompt kind is invalid."),
            };
        }

        Kind = kind;
    }

    public BreakPromptKind Kind { get; }

    public string Text { get; }
}

public sealed record BreakPlan
{
    public BreakPlan(TimeSpan duration, BreakPrompt prompt)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Break duration must be positive.", nameof(duration));
        }

        Duration = duration;
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    }

    public TimeSpan Duration { get; }

    public BreakPrompt Prompt { get; }
}
