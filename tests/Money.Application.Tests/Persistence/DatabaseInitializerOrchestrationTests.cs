using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Money.Application.Abstractions;
using Money.Infrastructure.Persistence;

namespace Money.Application.Tests.Persistence;

/// <summary>
/// Pins <see cref="DatabaseInitializer.InitialiseAsync"/> itself. The brief's own
/// <see cref="DatabaseInitializerTests"/> never instantiates the class - it only exercises
/// <c>SqliteFixture</c>'s connection and migration idempotence - so backup-strictly-before-migrate,
/// the property this task exists to guarantee, previously rested entirely on reading the code.
///
/// Each test builds its own raw, unmigrated SQLite connection rather than using
/// <c>SqliteFixture</c>, because the fixture's constructor already runs every migration - leaving
/// nothing pending to back up before. A fake "already applied" row is inserted directly into
/// <c>__EFMigrationsHistory</c> to put the database in the "has history, but the real schema
/// migration is still pending" state the backup guard cares about, without needing a second real
/// migration to exist.
/// </summary>
public sealed class DatabaseInitializerOrchestrationTests
{
    [Fact]
    public async Task The_backup_runs_before_the_migration_when_the_database_already_has_history()
    {
        using var connection = OpenConnectionWithFakeMigrationHistory();
        using var context = NewContext(connection);
        var backupService = new RecordingBackupService(connection);
        var initializer = new DatabaseInitializer(context, backupService, NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitialiseAsync();

        backupService.WasCalled.Should().BeTrue("a database with existing history has something to lose");
        backupService.AccountsTableExistedAtBackupTime.Should().BeFalse(
            "the backup must be taken before the migration creates the schema, not after");
        TableExists(connection, "Accounts").Should().BeTrue("the migration must still run, after the backup");
    }

    [Fact]
    public async Task A_backup_failure_stops_the_migration_from_running()
    {
        using var connection = OpenConnectionWithFakeMigrationHistory();
        using var context = NewContext(connection);
        var initializer = new DatabaseInitializer(
            context, new ThrowingBackupService(), NullLogger<DatabaseInitializer>.Instance);

        var act = async () => await initializer.InitialiseAsync();

        await act.Should().ThrowAsync<InvalidOperationException>("a failed backup must fail closed");
        TableExists(connection, "Accounts").Should().BeFalse(
            "the migration must never run once the pre-migration backup has failed");
    }

    [Fact]
    public async Task A_brand_new_database_with_no_history_is_migrated_without_a_backup()
    {
        using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        connection.Open();
        using var context = NewContext(connection);
        var backupService = new RecordingBackupService(connection);
        var initializer = new DatabaseInitializer(context, backupService, NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitialiseAsync();

        backupService.WasCalled.Should().BeFalse(
            "there is nothing yet to back up on a machine's very first run (DatabaseInitializer's alreadyApplied.Any() guard)");
        TableExists(connection, "Accounts").Should().BeTrue("the migration must still run on a brand-new database");
    }

    private static SqliteConnection OpenConnectionWithFakeMigrationHistory()
    {
        var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('00000000000000_FakeBaseline', '9.0.0');
            """;
        command.ExecuteNonQuery();

        return connection;
    }

    private static MoneyDbContext NewContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<MoneyDbContext>().UseSqlite(connection).Options);

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private sealed class RecordingBackupService(SqliteConnection connection) : IBackupService
    {
        public bool WasCalled { get; private set; }
        public bool AccountsTableExistedAtBackupTime { get; private set; }

        public Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            AccountsTableExistedAtBackupTime = TableExists(connection, "Accounts");
            return Task.FromResult("backup.db");
        }

        public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BackupInfo>>([]);
    }

    private sealed class ThrowingBackupService : IBackupService
    {
        public Task<string> CreateBackupAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated backup failure");

        public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BackupInfo>>([]);
    }
}
