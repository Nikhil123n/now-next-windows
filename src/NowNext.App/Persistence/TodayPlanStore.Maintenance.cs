using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NowNext.App.Persistence;

public sealed partial class TodayPlanStore
{
    internal string DatabasePath => _databasePath;

    internal static async System.Threading.Tasks.Task ValidateDatabaseFileAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidDataException("The selected database file does not exist.");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        };
        await using var connection = new SqliteConnection(connectionString.ToString());
        await connection.OpenAsync(cancellationToken);

        await using (SqliteCommand integrityCommand = connection.CreateCommand())
        {
            integrityCommand.CommandText = "PRAGMA quick_check;";
            object? result = await integrityCommand.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(
                    Convert.ToString(result, CultureInfo.InvariantCulture),
                    "ok",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The selected database failed its integrity check.");
            }
        }

        await using (SqliteCommand foreignKeyCommand = connection.CreateCommand())
        {
            foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
            await using SqliteDataReader reader = await foreignKeyCommand.ExecuteReaderAsync(
                cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidDataException(
                    "The selected database failed its foreign-key integrity check.");
            }
        }

        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        IReadOnlyList<AppliedMigration> appliedMigrations =
            await ReadAppliedMigrationsAsync(connection, transaction, cancellationToken);
        try
        {
            ValidateAppliedMigrations(appliedMigrations);
        }
        catch (TodayPlanStorageException exception)
        {
            throw new InvalidDataException(
                "The selected database has an incompatible migration history.",
                exception);
        }

        if (appliedMigrations.Count != Migrations.Length)
        {
            throw new InvalidDataException(
                "The selected database is not at the current schema version.");
        }
    }

    internal static async System.Threading.Tasks.Task BackupDatabaseFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        string sourceFullPath = Path.GetFullPath(sourcePath);
        string destinationFullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The backup destination must differ from its source.",
                nameof(destinationPath));
        }

        string? destinationDirectory = Path.GetDirectoryName(destinationFullPath);
        if (string.IsNullOrEmpty(destinationDirectory))
        {
            throw new ArgumentException("The backup destination directory is invalid.", nameof(destinationPath));
        }

        Directory.CreateDirectory(destinationDirectory);
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourceFullPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        };
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationFullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        };
        await using var source = new SqliteConnection(sourceConnectionString.ToString());
        await using var destination = new SqliteConnection(destinationConnectionString.ToString());
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        source.BackupDatabase(destination);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
