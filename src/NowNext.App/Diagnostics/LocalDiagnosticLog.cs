using System.Text.Json;
using NowNext.App.WindowsIntegration;

namespace NowNext.App.Diagnostics;

public enum DiagnosticEventId
{
    AppStarted,
    AppStopped,
    StorageInitialized,
    SuspendCheckpoint,
    ResumeRecovery,
    BackupCreated,
    ExportCreated,
    RestoreCompleted,
}

public enum DiagnosticResult
{
    Succeeded,
    Failed,
}

public interface ILocalDiagnosticLog
{
    public System.Threading.Tasks.Task WriteAsync(
        DiagnosticEventId eventId,
        DiagnosticResult result,
        Exception? exception = null,
        CancellationToken cancellationToken = default);
}

public sealed class LocalDiagnosticLog : ILocalDiagnosticLog, IDisposable
{
    private const long MaximumLogBytes = 1024 * 1024;
    private readonly string _logPath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _disposed;

    public LocalDiagnosticLog(
        IApplicationDataPaths paths,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _logPath = EnsureDescendant(paths.LocalStatePath, paths.DiagnosticLogPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async System.Threading.Tasks.Task WriteAsync(
        DiagnosticEventId eventId,
        DiagnosticResult result,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(eventId))
        {
            throw new ArgumentOutOfRangeException(nameof(eventId));
        }

        if (!Enum.IsDefined(result))
        {
            throw new ArgumentOutOfRangeException(nameof(result));
        }

        var entry = new DiagnosticEntry(
            _timeProvider.GetUtcNow().ToUniversalTime(),
            eventId.ToString(),
            result.ToString(),
            exception?.GetType().FullName);
        string line = JsonSerializer.Serialize(entry) + Environment.NewLine;

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            string directoryPath = Path.GetDirectoryName(_logPath)
                ?? throw new InvalidOperationException("The diagnostic directory is invalid.");
            Directory.CreateDirectory(directoryPath);
            RotateIfNeeded();
            await File.AppendAllTextAsync(_logPath, line, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _writeGate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void RotateIfNeeded()
    {
        var logFile = new FileInfo(_logPath);
        if (!logFile.Exists || logFile.Length < MaximumLogBytes)
        {
            return;
        }

        string previousPath = _logPath + ".previous";
        if (File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }

        File.Move(_logPath, previousPath);
    }

    private static string EnsureDescendant(string rootPath, string candidatePath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string candidate = Path.GetFullPath(candidatePath);
        string relative = Path.GetRelativePath(root, candidate);
        if (relative == "."
            || Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The diagnostic log must be below the application-data directory.",
                nameof(candidatePath));
        }

        return candidate;
    }

    private sealed record DiagnosticEntry(
        DateTimeOffset TimestampUtc,
        string Event,
        string Result,
        string? ExceptionType);
}
