using Microsoft.Data.Sqlite;
using NowNext.App.Persistence;

namespace NowNext.Core.Tests.Persistence;

[TestClass]
public sealed class MigrationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 18, 13, 45, 30, TimeSpan.Zero);

    private readonly TestContext _testContext;

    public MigrationTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task InitializeEmptyDatabaseRecordsBothMigrationsOnce()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);

        await store.InitializeAsync(_testContext.CancellationToken);
        await store.InitializeAsync(_testContext.CancellationToken);
        (long version, string name, long count) = await ReadMigrationAsync(
            database,
            _testContext.CancellationToken);

        Assert.AreEqual(2L, version);
        Assert.AreEqual("current_focus_session_checkpoint", name);
        Assert.AreEqual(2L, count);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task InitializeVersionOneDatabaseAppliesVersionTwo()
    {
        using var database = new TestDatabase();
        await CreateVersionOneDatabaseAsync(database, _testContext.CancellationToken);
        using var store = CreateStore(database);

        await store.InitializeAsync(_testContext.CancellationToken);
        (long version, string name, long count) = await ReadMigrationAsync(
            database,
            _testContext.CancellationToken);
        long checkpointTableCount = await ReadSchemaObjectCountAsync(
            database,
            "current_session_checkpoint",
            _testContext.CancellationToken);

        Assert.AreEqual(2L, version);
        Assert.AreEqual("current_focus_session_checkpoint", name);
        Assert.AreEqual(2L, count);
        Assert.AreEqual(1L, checkpointTableCount);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task InitializeConflictingPartialSchemaRollsBackMigrationTable()
    {
        using var database = new TestDatabase();
        await ExecuteAsync(
            database,
            "CREATE TABLE tasks (task_id TEXT PRIMARY KEY);",
            _testContext.CancellationToken);
        using var store = CreateStore(database);

        await Assert.ThrowsExactlyAsync<TodayPlanStorageException>(
            async () => await store.InitializeAsync(_testContext.CancellationToken));
        long migrationTableCount = await ReadSchemaObjectCountAsync(
            database,
            "schema_migrations",
            _testContext.CancellationToken);
        long originalTableCount = await ReadSchemaObjectCountAsync(
            database,
            "tasks",
            _testContext.CancellationToken);

        Assert.AreEqual(0L, migrationTableCount);
        Assert.AreEqual(1L, originalTableCount);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task InitializeConflictingVersionTwoSchemaRollsBackMigration()
    {
        using var database = new TestDatabase();
        await CreateVersionOneDatabaseAsync(database, _testContext.CancellationToken);
        await ExecuteAsync(
            database,
            "CREATE TABLE current_session_checkpoint (sentinel TEXT NOT NULL);",
            _testContext.CancellationToken);
        using var store = CreateStore(database);

        await Assert.ThrowsExactlyAsync<TodayPlanStorageException>(
            async () => await store.InitializeAsync(_testContext.CancellationToken));
        (long version, string name, long count) = await ReadMigrationAsync(
            database,
            _testContext.CancellationToken);

        Assert.AreEqual(1L, version);
        Assert.AreEqual("initial_today_plan", name);
        Assert.AreEqual(1L, count);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task InitializeUnknownFutureVersionThrowsUnderstandableFailure()
    {
        using var database = new TestDatabase();
        await ExecuteAsync(
            database,
            """
            CREATE TABLE schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                applied_utc TEXT NOT NULL
            );
            INSERT INTO schema_migrations(version, name, applied_utc)
            VALUES (3, 'future', '2026-07-18T00:00:00.0000000+00:00');
            """,
            _testContext.CancellationToken);
        using var store = CreateStore(database);

        TodayPlanStorageException exception =
            await Assert.ThrowsExactlyAsync<TodayPlanStorageException>(
                async () => await store.InitializeAsync(_testContext.CancellationToken));

        Assert.Contains("version 3", exception.Message);
        Assert.Contains("unknown or non-contiguous", exception.Message);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task InitializedSchemaEnforcesTaskForeignKey()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        await store.InitializeAsync(_testContext.CancellationToken);
        await ExecuteAsync(
            database,
            "INSERT INTO today_plans(plan_date) VALUES ('2026-07-18');",
            _testContext.CancellationToken);

        SqliteException exception = await Assert.ThrowsExactlyAsync<SqliteException>(
            async () => await ExecuteAsync(
                database,
                """
                INSERT INTO schedule_entries(plan_date, task_id, position)
                VALUES ('2026-07-18', '00000000-0000-0000-0000-000000000001', 0);
                """,
                _testContext.CancellationToken));

        Assert.AreEqual(19, exception.SqliteErrorCode);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task InitializedSchemaPassesForeignKeyIntegrityCheck()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        await store.InitializeAsync(_testContext.CancellationToken);

        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(_testContext.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(_testContext.CancellationToken);

        Assert.IsFalse(await reader.ReadAsync(_testContext.CancellationToken));
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async System.Threading.Tasks.Task VersionTwoSchemaEnforcesCurrentSessionTaskForeignKey()
    {
        using var database = new TestDatabase();
        using var store = CreateStore(database);
        await store.InitializeAsync(_testContext.CancellationToken);

        SqliteException exception = await Assert.ThrowsExactlyAsync<SqliteException>(
            async () => await ExecuteAsync(
                database,
                """
                INSERT INTO current_session_checkpoint(
                    slot,
                    session_id,
                    task_id,
                    timing_mode,
                    original_planned_duration_ticks,
                    approved_limit_ticks,
                    session_state,
                    committed_active_ticks,
                    landing_ticks,
                    break_ticks,
                    checkpointed_at_utc)
                VALUES (
                    1,
                    '00000000-0000-0000-0000-000000000001',
                    '00000000-0000-0000-0000-000000000002',
                    'CountUp',
                    1,
                    1,
                    'Ready',
                    0,
                    0,
                    0,
                    '2026-07-18T00:00:00.0000000+00:00');
                """,
                _testContext.CancellationToken));

        Assert.AreEqual(19, exception.SqliteErrorCode);
    }

    private static TodayPlanStore CreateStore(TestDatabase database)
    {
        return new TodayPlanStore(database.DatabasePath, new FixedTimeProvider(FixedNow));
    }

    private static async System.Threading.Tasks.Task ExecuteAsync(
        TestDatabase database,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async System.Threading.Tasks.Task CreateVersionOneDatabaseAsync(
        TestDatabase database,
        CancellationToken cancellationToken)
    {
        const string resourceName =
            "NowNext.App.Persistence.Migrations.0001_initial_today_plan.sql";
        await using Stream stream = typeof(TodayPlanStore).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The version 1 migration is missing.");
        using var reader = new StreamReader(stream);
        string migrationSql = await reader.ReadToEndAsync(cancellationToken);
        await ExecuteAsync(
            database,
            $"""
            CREATE TABLE schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                applied_utc TEXT NOT NULL
            );
            {migrationSql}
            INSERT INTO schema_migrations(version, name, applied_utc)
            VALUES (1, 'initial_today_plan', '2026-07-18T00:00:00.0000000+00:00');
            """,
            cancellationToken);
    }

    private static async System.Threading.Tasks.Task<(long Version, string Name, long Count)>
        ReadMigrationAsync(TestDatabase database, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT version,
                   name,
                   (SELECT COUNT(*) FROM schema_migrations)
            FROM schema_migrations
            ORDER BY version DESC
            LIMIT 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2));
    }

    private static async System.Threading.Tasks.Task<long> ReadSchemaObjectCountAsync(
        TestDatabase database,
        string objectName,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_schema
            WHERE type = 'table'
              AND name = $name;
            """;
        command.Parameters.AddWithValue("$name", objectName);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
