namespace NowNext.App.Persistence;

public sealed class TodayPlanStorageException : Exception
{
    public TodayPlanStorageException(string message)
        : base(message)
    {
    }

    public TodayPlanStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
