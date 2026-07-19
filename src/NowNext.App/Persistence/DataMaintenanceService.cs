using System.Globalization;
using NowNext.App.Diagnostics;
using NowNext.App.WindowsIntegration;

namespace NowNext.App.Persistence;

public sealed class DataMaintenanceService : IDisposable
{
    private readonly IApplicationDataPaths _paths;
    private readonly ILocalDiagnosticLog _diagnosticLog;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public DataMaintenanceService(
        IApplicationDataPaths paths,
        ILocalDiagnosticLog diagnosticLog,
        TimeProvider? timeProvider = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ = EnsureDescendant(_paths.LocalStatePath, _paths.DatabasePath, nameof(paths));
        _ = EnsureDescendant(_paths.LocalStatePath, _paths.BackupDirectoryPath, nameof(paths));
        _ = EnsureDescendant(_paths.LocalStatePath, _paths.ExportDirectoryPath, nameof(paths));
        _ = EnsureDescendant(_paths.LocalStatePath, _paths.DiagnosticDirectoryPath, nameof(paths));
    }

    public async System.Threading.Tasks.Task<string> CreateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.BackupDirectoryPath);
            string timestamp = _timeProvider.GetUtcNow().ToUniversalTime().ToString(
                "yyyyMMddTHHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            string destinationPath = Path.Combine(
                _paths.BackupDirectoryPath,
                $"now-next-backup-{timestamp}.db");
            destinationPath = GetAvailablePath(destinationPath);
            await CreateValidatedCopyAsync(
                _paths.DatabasePath,
                destinationPath,
                cancellationToken);
            await WriteDiagnosticAsync(
                DiagnosticEventId.BackupCreated,
                DiagnosticResult.Succeeded,
                cancellationToken: cancellationToken);
            return destinationPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsMaintenanceFailure(exception))
        {
            await WriteDiagnosticAsync(
                DiagnosticEventId.BackupCreated,
                DiagnosticResult.Failed,
                exception,
                cancellationToken);
            throw new DataMaintenanceException(
                "NOW/NEXT could not create a validated local backup.",
                exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async System.Threading.Tasks.Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            string destination = Path.GetFullPath(destinationPath);
            if (string.Equals(destination, _paths.DatabasePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The live database cannot be used as its own export destination.",
                    nameof(destinationPath));
            }

            await CreateValidatedCopyAsync(
                _paths.DatabasePath,
                destination,
                cancellationToken);
            await WriteDiagnosticAsync(
                DiagnosticEventId.ExportCreated,
                DiagnosticResult.Succeeded,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsMaintenanceFailure(exception))
        {
            await WriteDiagnosticAsync(
                DiagnosticEventId.ExportCreated,
                DiagnosticResult.Failed,
                exception,
                cancellationToken);
            throw new DataMaintenanceException(
                "NOW/NEXT could not export a validated local database.",
                exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async System.Threading.Tasks.Task RestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            string source = Path.GetFullPath(sourcePath);
            if (string.Equals(source, _paths.DatabasePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The live database cannot be restored from itself.",
                    nameof(sourcePath));
            }

            await RestoreValidatedSourceAsync(source, cancellationToken);
            await WriteDiagnosticAsync(
                DiagnosticEventId.RestoreCompleted,
                DiagnosticResult.Succeeded,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsMaintenanceFailure(exception))
        {
            await WriteDiagnosticAsync(
                DiagnosticEventId.RestoreCompleted,
                DiagnosticResult.Failed,
                exception,
                cancellationToken);
            throw new DataMaintenanceException(
                "NOW/NEXT could not restore the selected local database.",
                exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async System.Threading.Tasks.Task ResetAsync(
        IWindowsUserSettings userSettings,
        ILaunchAtSignInService launchAtSignInService,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(userSettings);
        ArgumentNullException.ThrowIfNull(launchAtSignInService);
        await _operationGate.WaitAsync(cancellationToken);
        string freshPath = CreateTemporaryLocalPath("reset-fresh");
        try
        {
            using (var freshStore = new TodayPlanStore(freshPath, _timeProvider))
            {
                await freshStore.InitializeAsync(cancellationToken);
            }

            await RestoreValidatedSourceAsync(freshPath, cancellationToken);
            DeleteLocalDataDirectory(_paths.BackupDirectoryPath);
            DeleteLocalDataDirectory(_paths.ExportDirectoryPath);
            DeleteLocalDataDirectory(_paths.DiagnosticDirectoryPath);
            DeleteDatabaseSidecars(_paths.DatabasePath);
            userSettings.Clear();
            try
            {
                _ = await launchAtSignInService.SetEnabledAsync(false);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or UnauthorizedAccessException
                    or System.Runtime.InteropServices.ExternalException)
            {
                // Windows policy or Task Manager may own the startup state.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsMaintenanceFailure(exception))
        {
            throw new DataMaintenanceException(
                "NOW/NEXT could not completely reset its local data.",
                exception);
        }
        finally
        {
            DeleteFileIfPresent(freshPath);
            DeleteDatabaseSidecars(freshPath);
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _operationGate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async System.Threading.Tasks.Task RestoreValidatedSourceAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await TodayPlanStore.ValidateDatabaseFileAsync(sourcePath, cancellationToken);
        string stagedPath = CreateTemporaryLocalPath("restore-staged");
        string rollbackPath = CreateTemporaryLocalPath("restore-rollback");
        bool liveReplacementStarted = false;
        try
        {
            await CreateValidatedCopyAsync(sourcePath, stagedPath, cancellationToken);
            await CreateValidatedCopyAsync(
                _paths.DatabasePath,
                rollbackPath,
                cancellationToken);
            liveReplacementStarted = true;
            await TodayPlanStore.BackupDatabaseFileAsync(
                stagedPath,
                _paths.DatabasePath,
                cancellationToken);
            await TodayPlanStore.ValidateDatabaseFileAsync(
                _paths.DatabasePath,
                cancellationToken);
        }
        catch
        {
            if (liveReplacementStarted)
            {
                await TodayPlanStore.BackupDatabaseFileAsync(
                    rollbackPath,
                    _paths.DatabasePath,
                    CancellationToken.None);
                await TodayPlanStore.ValidateDatabaseFileAsync(
                    _paths.DatabasePath,
                    CancellationToken.None);
            }

            throw;
        }
        finally
        {
            DeleteFileIfPresent(stagedPath);
            DeleteDatabaseSidecars(stagedPath);
            DeleteFileIfPresent(rollbackPath);
            DeleteDatabaseSidecars(rollbackPath);
        }
    }

    private static async System.Threading.Tasks.Task CreateValidatedCopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await TodayPlanStore.ValidateDatabaseFileAsync(sourcePath, cancellationToken);
        string destination = Path.GetFullPath(destinationPath);
        string temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await TodayPlanStore.BackupDatabaseFileAsync(
                sourcePath,
                temporaryPath,
                cancellationToken);
            await TodayPlanStore.ValidateDatabaseFileAsync(temporaryPath, cancellationToken);
            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            DeleteFileIfPresent(temporaryPath);
        }
    }

    private async System.Threading.Tasks.Task WriteDiagnosticAsync(
        DiagnosticEventId eventId,
        DiagnosticResult result,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _diagnosticLog.WriteAsync(
                eventId,
                result,
                exception,
                cancellationToken);
        }
        catch (Exception diagnosticException) when (
            diagnosticException is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ObjectDisposedException)
        {
            // Diagnostics are best effort and never invalidate a successful database copy.
        }
    }

    private string CreateTemporaryLocalPath(string purpose)
    {
        string temporaryDirectory = EnsureDescendant(
            _paths.LocalStatePath,
            Path.Combine(_paths.LocalStatePath, "Maintenance"),
            nameof(_paths));
        Directory.CreateDirectory(temporaryDirectory);
        return Path.Combine(
            temporaryDirectory,
            $".{purpose}-{Guid.NewGuid():N}.db");
    }

    private void DeleteLocalDataDirectory(string directoryPath)
    {
        string directory = EnsureDescendant(
            _paths.LocalStatePath,
            directoryPath,
            nameof(directoryPath));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private void DeleteDatabaseSidecars(string databasePath)
    {
        string database = EnsureDescendant(
            _paths.LocalStatePath,
            databasePath,
            nameof(databasePath));
        DeleteFileIfPresent(database + "-shm");
        DeleteFileIfPresent(database + "-wal");
        DeleteFileIfPresent(database + "-journal");
    }

    private static void DeleteFileIfPresent(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static string GetAvailablePath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
        {
            return desiredPath;
        }

        string directory = Path.GetDirectoryName(desiredPath)
            ?? throw new InvalidOperationException("The backup directory is invalid.");
        string name = Path.GetFileNameWithoutExtension(desiredPath);
        string extension = Path.GetExtension(desiredPath);
        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidate = Path.Combine(directory, $"{name}-{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("A unique local backup filename could not be created.");
    }

    private static string EnsureDescendant(
        string rootPath,
        string candidatePath,
        string parameterName)
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
                "The maintenance path must be below the exact application-data directory.",
                parameterName);
        }

        return candidate;
    }

    private static bool IsMaintenanceFailure(Exception exception)
    {
        return exception is
            ArgumentException
            or InvalidDataException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or Microsoft.Data.Sqlite.SqliteException
            or TodayPlanStorageException;
    }
}
