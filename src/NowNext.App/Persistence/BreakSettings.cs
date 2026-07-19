namespace NowNext.App.Persistence;

public sealed record BreakSettings
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(5);

    public BreakSettings(TimeSpan defaultDuration, string? userSelectedMovement = null)
    {
        if (defaultDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Default Break duration must be positive.",
                nameof(defaultDuration));
        }

        DefaultBreakDuration = defaultDuration;
        UserSelectedMovement = string.IsNullOrWhiteSpace(userSelectedMovement)
            ? null
            : userSelectedMovement.Trim();
    }

    public TimeSpan DefaultBreakDuration { get; }

    public string? UserSelectedMovement { get; }

    public static BreakSettings CreateDefault()
    {
        return new BreakSettings(DefaultDuration);
    }
}
