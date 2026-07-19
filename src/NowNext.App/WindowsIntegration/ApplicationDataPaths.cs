using Windows.Storage;

namespace NowNext.App.WindowsIntegration;

public interface IApplicationDataPaths
{
    public string LocalStatePath { get; }

    public string DatabasePath { get; }

    public string BackupDirectoryPath { get; }

    public string ExportDirectoryPath { get; }

    public string DiagnosticDirectoryPath { get; }

    public string DiagnosticLogPath { get; }
}

public sealed class WindowsApplicationDataPaths : IApplicationDataPaths
{
    private const string DatabaseFileName = "now-next.db";
    private const string DiagnosticFileName = "now-next.log.jsonl";

    public WindowsApplicationDataPaths()
        : this(ApplicationData.Current.LocalFolder.Path)
    {
    }

    internal WindowsApplicationDataPaths(string localStatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localStatePath);
        LocalStatePath = Path.GetFullPath(localStatePath);
        DatabasePath = Path.Combine(LocalStatePath, DatabaseFileName);
        BackupDirectoryPath = Path.Combine(LocalStatePath, "Backups");
        ExportDirectoryPath = Path.Combine(LocalStatePath, "Exports");
        DiagnosticDirectoryPath = Path.Combine(LocalStatePath, "Diagnostics");
        DiagnosticLogPath = Path.Combine(DiagnosticDirectoryPath, DiagnosticFileName);
    }

    public string LocalStatePath { get; }

    public string DatabasePath { get; }

    public string BackupDirectoryPath { get; }

    public string ExportDirectoryPath { get; }

    public string DiagnosticDirectoryPath { get; }

    public string DiagnosticLogPath { get; }
}
