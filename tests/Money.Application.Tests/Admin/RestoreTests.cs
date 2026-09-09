using CsCheck;
using Microsoft.Data.Sqlite;
using Money.Application.Admin;
using Money.Infrastructure.Backup;
using Money.Infrastructure.Persistence;
using Money.Infrastructure.Persistence.Repositories;
using Money.TestSupport;

namespace Money.Application.Tests.Admin;

/// <summary>
/// Restore is the one operation that can destroy a ledger, so every rejection is a test and the
/// live file is asserted untouched. A real temp directory, not a mock file system: the staging
/// step is file handling, and file handling is what would go wrong.
/// </summary>
public sealed class RestoreTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "money-tests", Guid.NewGuid().ToString("N"));

    private string LivePath => DataDirectory.DatabasePathIn(_dataDirectory);
    private string BackupDirectory => DataDirectory.BackupDirectoryIn(_dataDirectory);
    private string PendingPath => DatabaseRestore.PendingPathIn(_dataDirectory);
    private StageRestoreHandler Handler => new(new DatabaseRestore(_dataDirectory));

    public RestoreTests() => Directory.CreateDirectory(BackupDirectory);

    public void Dispose()
    {
        _fixture.Dispose();

        // Microsoft.Data.Sqlite pools native connections past Dispose, which holds a Windows file
        // lock on anything this test opened - the same reason BackupAndExportTests clears pools.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    [Fact]
    public async Task Staging_accepts_a_backup_written_by_the_backup_service()
    {
        var fileName = await WriteRealBackupAsync();

        var result = Handler.Handle(fileName);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        File.Exists(PendingPath).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"..\money-20260901-143000.db")]
    [InlineData("../money-20260901-143000.db")]
    [InlineData("nested/money-20260901-143000.db")]
    [InlineData(@"nested\money-20260901-143000.db")]
    [InlineData(@"C:\Windows\money.db")]
    [InlineData("..")]
    [InlineData("")]
    public void Staging_rejects_anything_that_is_not_a_plain_file_name(string fileName)
    {
        var result = Handler.Handle(fileName);

        result.Error!.Code.Should().Be("admin.invalid_backup_name");
        File.Exists(PendingPath).Should().BeFalse();
    }

    [Fact]
    public void Staging_rejects_a_file_that_is_not_there()
    {
        var result = Handler.Handle("money-19990101-000000.db");

        result.Error!.Code.Should().Be("admin.backup.not_found");
        File.Exists(PendingPath).Should().BeFalse();
    }

    [Fact]
    public void Staging_rejects_a_file_that_is_not_a_database()
    {
        File.WriteAllText(Path.Combine(BackupDirectory, "money-notadb.db"), "I am a text file.");

        var result = Handler.Handle("money-notadb.db");

        result.Error!.Code.Should().Be("admin.backup_not_restorable");
        File.Exists(PendingPath).Should().BeFalse();
    }

    [Fact]
    public void Staging_rejects_a_database_with_no_migrations_history()
    {
        var path = Path.Combine(BackupDirectory, "money-nohistory.db");
        Execute(path, "CREATE TABLE Something (Id TEXT)");

        var result = Handler.Handle("money-nohistory.db");

        result.Error!.Code.Should().Be("admin.backup_not_restorable");
    }

    [Fact]
    public void Staging_rejects_a_database_whose_migrations_history_is_empty()
    {
        var path = Path.Combine(BackupDirectory, "money-emptyhistory.db");
        Execute(path, MigrationsHistorySql);

        var result = Handler.Handle("money-emptyhistory.db");

        result.Error!.Code.Should().Be("admin.backup_not_restorable");
    }

    [Fact]
    public async Task Staging_leaves_the_live_database_byte_identical()
    {
        WriteDatabase(LivePath, "LIVE");
        var before = File.ReadAllBytes(LivePath);
        var fileName = await WriteRealBackupAsync();

        Handler.Handle(fileName).IsSuccess.Should().BeTrue();

        File.ReadAllBytes(LivePath).Should().Equal(before);
    }

    [Fact]
    public void Applying_a_pending_restore_replaces_the_live_database_and_clears_the_staged_file()
    {
        WriteDatabase(LivePath, "LIVE");
        WriteDatabase(PendingPath, "STAGED");
        File.WriteAllText(LivePath + "-wal", "stale");
        File.WriteAllText(LivePath + "-shm", "stale");

        DatabaseRestore.ApplyPending(_dataDirectory, FakeClock.At(2026, 9, 9, 10, 0).UtcNow)
            .Should().BeTrue();

        MarkerIn(LivePath).Should().Be("STAGED");
        File.Exists(PendingPath).Should().BeFalse();
        File.Exists(LivePath + "-wal").Should().BeFalse();
        File.Exists(LivePath + "-shm").Should().BeFalse();
    }

    [Fact]
    public void Applying_with_nothing_staged_is_a_no_op()
    {
        WriteDatabase(LivePath, "LIVE");

        DatabaseRestore.ApplyPending(_dataDirectory, FakeClock.At(2026, 9, 9, 10, 0).UtcNow)
            .Should().BeFalse();

        MarkerIn(LivePath).Should().Be("LIVE");
        Directory.EnumerateFiles(BackupDirectory).Should().BeEmpty();
    }

    [Fact]
    public void Applying_backs_up_the_pre_restore_database_first()
    {
        WriteDatabase(LivePath, "LIVE");
        WriteDatabase(PendingPath, "STAGED");

        DatabaseRestore.ApplyPending(_dataDirectory, FakeClock.At(2026, 9, 9, 10, 0).UtcNow);

        var written = Directory.EnumerateFiles(BackupDirectory).Should().ContainSingle().Subject;
        Path.GetFileName(written).Should().Be("money-20260909-100000-before-restore.db");
        MarkerIn(written).Should().Be("LIVE",
            "the copy taken before the swap must be the data that was about to be replaced");
    }

    private async Task<string> WriteRealBackupAsync()
    {
        using var context = _fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var service = new SqliteBackupService(
            context, new BackupOptions(_dataDirectory, RetentionCount: 10),
            new SettingsRepository(context), FakeClock.At(2026, 9, 1, 14, 30));

        return Path.GetFileName(await service.CreateBackupAsync());
    }

    private const string MigrationsHistorySql =
        "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT)";

    /// <summary>A minimal database that passes validation, tagged so the file can be identified.</summary>
    private static void WriteDatabase(string path, string marker) => Execute(path,
        MigrationsHistorySql + $"; INSERT INTO __EFMigrationsHistory VALUES ('{marker}', '9.0.0')");

    private static string MarkerIn(string path)
    {
        using var connection = Connect(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
        return (string)command.ExecuteScalar()!;
    }

    private static void Execute(string path, string sql)
    {
        using var connection = Connect(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Connect(string path)
    {
        var connection = new SqliteConnection($"DataSource={path};Pooling=False");
        connection.Open();
        return connection;
    }
}
