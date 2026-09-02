using CsCheck;
using Money.Infrastructure.Persistence;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

public sealed class IntegrityCheckerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_healthy_ledger_reports_no_findings()
    {
        using var context = _fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var report = await new IntegrityChecker(context).CheckAsync();

        report.IsHealthy.Should().BeTrue();
        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unbalanced_transaction_smuggled_in_through_raw_sql_is_detected()
    {
        using var context = _fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        // The domain cannot produce this; only a corrupted file or a bad migration could.
        // Microsoft.Data.Sqlite stores Guid columns as uppercase TEXT while Guid.ToString() is
        // lowercase, so the match must be case-insensitive or it silently touches zero rows.
        var victim = ledger.Transactions[0].Id;
        using (var command = _fixture.Connection.CreateCommand())
        {
            command.CommandText =
                $"UPDATE Postings SET AmountMinor = AmountMinor + 1 WHERE UPPER(TransactionId) = UPPER('{victim}') " +
                "AND rowid = (SELECT MIN(rowid) FROM Postings WHERE UPPER(TransactionId) = UPPER(" +
                $"'{victim}'))";
            command.ExecuteNonQuery();
        }

        var report = await new IntegrityChecker(context).CheckAsync();

        report.IsHealthy.Should().BeFalse();
        report.Findings.Should().ContainSingle(f => f.Check == "zero-sum");
        report.Findings[0].Detail.Should().Contain(victim.ToString());
    }

    [Fact]
    public async Task A_transaction_with_a_single_entry_is_detected()
    {
        using var context = _fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        var victim = ledger.Transactions[0].Id;
        using (var command = _fixture.Connection.CreateCommand())
        {
            command.CommandText =
                $"DELETE FROM Postings WHERE UPPER(TransactionId) = UPPER('{victim}') " +
                $"AND rowid > (SELECT MIN(rowid) FROM Postings WHERE UPPER(TransactionId) = UPPER('{victim}'))";
            command.ExecuteNonQuery();
        }

        var report = await new IntegrityChecker(context).CheckAsync();

        report.IsHealthy.Should().BeFalse();
        report.Findings.Should().Contain(f => f.Check == "entry-count");
    }
}
