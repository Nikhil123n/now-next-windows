namespace NowNext.App.Persistence;

public sealed class DataMaintenanceException : Exception
{
    public DataMaintenanceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
