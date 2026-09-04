using System.Text.Json;
using CsCheck;
using Money.Application.Abstractions;
using Money.Infrastructure.Backup;
using Money.Infrastructure.Export;
using Money.TestSupport;

namespace Money.Application.Tests.Admin;

public sealed class BackupAndExportTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "money-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _fixture.Dispose();

        // Microsoft.Data.Sqlite pools native connections even after Dispose, which holds a
        // Windows file lock on any backup file this test opened directly. Clear the pool before
        // deleting the temp directory, as SqlitePragmaInterceptorTests already does.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public async Task A_backup_writes_a_timestamped_file_into_the_backups_folder()
    {
        using var context = _fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var service = new SqliteBackupService(
            context, new BackupOptions(_tempDirectory, RetentionCount: 10), FakeClock.At(2026, 9, 1, 14, 30));

        var path = await service.CreateBackupAsync();

        File.Exists(path).Should().BeTrue();
        Path.GetFileName(path).Should().Be("money-20260901-143000.db");
        new FileInfo(path).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Old_backups_beyond_the_retention_count_are_removed()
    {
        using var context = _fixture.NewContext();
        var clock = FakeClock.At(2026, 9, 1, 0, 0);
        var service = new SqliteBackupService(
            context, new BackupOptions(_tempDirectory, RetentionCount: 3), clock);

        for (var i = 0; i < 5; i++)
        {
            await service.CreateBackupAsync();
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        (await service.ListBackupsAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task A_backup_is_a_readable_database_containing_the_same_rows()
    {
        using var context = _fixture.NewContext();
        var ledger = await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var service = new SqliteBackupService(
            context, new BackupOptions(_tempDirectory, 10), FakeClock.At(2026, 9, 1, 14, 30));
        var path = await service.CreateBackupAsync();

        using var restored = new Microsoft.Data.Sqlite.SqliteConnection($"DataSource={path}");
        restored.Open();
        using var command = restored.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Transactions";

        Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(ledger.Transactions.Count);
    }

    [Fact]
    public async Task The_json_export_contains_accounts_and_transactions_and_re_parses()
    {
        using var context = _fixture.NewContext();
        var ledger = await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var json = await new JsonExportService(context).ExportJsonAsync();

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("accounts").GetArrayLength()
            .Should().Be(ledger.Accounts.Count);
        document.RootElement.GetProperty("transactions").GetArrayLength()
            .Should().Be(ledger.Transactions.Count);
        document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task The_csv_export_is_a_flat_entry_table_with_a_header()
    {
        using var context = _fixture.NewContext();
        var ledger = await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var csv = await new CsvExportService(context).ExportCsvAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be(
            "TransactionId,OccurredOn,Description,Payee,IsVoided,AccountPath,AccountName," +
            "AmountMinor,CurrencyCode,Memo");
        lines.Should().HaveCount(1 + ledger.Transactions.Sum(t => t.Postings.Count));
    }

    [Fact]
    public async Task A_field_containing_a_comma_or_a_quote_is_escaped_in_the_csv()
    {
        using var context = _fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        // Microsoft.Data.Sqlite stores Guid columns as uppercase TEXT while Guid.ToString() is
        // lowercase (see IntegrityCheckerTests), so the match must be case-insensitive or it
        // silently touches zero rows.
        using (var command = _fixture.Connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE Transactions SET Description = 'Dinner, with \"friends\"' " +
                $"WHERE UPPER(Id) = UPPER('{ledger.Transactions[0].Id}')";
            command.ExecuteNonQuery();
        }

        var csv = await new CsvExportService(context).ExportCsvAsync();

        csv.Should().Contain("\"Dinner, with \"\"friends\"\"\"");
    }
}
