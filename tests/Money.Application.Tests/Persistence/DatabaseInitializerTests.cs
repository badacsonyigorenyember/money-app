using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Foreign_keys_are_on_for_every_connection()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";

        Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(1);
    }

    [Fact]
    public void Applying_the_migrations_twice_is_a_no_op()
    {
        using var context = _fixture.NewContext();

        var act = () => context.Database.Migrate();

        act.Should().NotThrow();
        context.Database.GetPendingMigrations().Should().BeEmpty();
    }

    [Fact]
    public void A_freshly_migrated_database_passes_the_integrity_check()
    {
        SeedOneBalancedTransaction();

        Convert.ToInt32(RunIntegrityCheck(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(0);
    }

    private object? RunIntegrityCheck()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM (SELECT TransactionId FROM Postings " +
            "GROUP BY TransactionId HAVING SUM(AmountMinor) <> 0)";
        return command.ExecuteScalar();
    }

    /// <summary>
    /// A balanced pair of postings so the integrity check above has something real to be right
    /// about - a database with zero postings would pass the same query trivially, proving nothing
    /// about the schema. Written as raw SQL, matching <c>SchemaConstraintTests</c>'s style, rather
    /// than the domain builders, because this file proves what the *database* accepts.
    /// </summary>
    private void SeedOneBalancedTransaction()
    {
        var bankId = Guid.NewGuid();
        var foodId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        Execute(
            "INSERT INTO Accounts (Id, Name, Kind, Role, Path, CurrencyCode, IsArchived, SortOrder, " +
            $"CreatedAtUtc, UpdatedAtUtc) VALUES ('{bankId}', 'Bank', 'Asset', 'Bank', '/asset/bank', " +
            "'EUR', 0, 0, '2026-09-01T00:00:00+00:00', '2026-09-01T00:00:00+00:00')");
        Execute(
            "INSERT INTO Accounts (Id, Name, Kind, Role, Path, CurrencyCode, IsArchived, SortOrder, " +
            $"CreatedAtUtc, UpdatedAtUtc) VALUES ('{foodId}', 'Food', 'Expense', 'Category', '/expense/food', " +
            "'EUR', 0, 0, '2026-09-01T00:00:00+00:00', '2026-09-01T00:00:00+00:00')");
        Execute(
            "INSERT INTO Transactions (Id, OccurredOn, BookedAtUtc, Description, SourceKind, SourceId, " +
            $"IsVoided, CreatedAtUtc, UpdatedAtUtc) VALUES ('{transactionId}', '2026-09-01', " +
            "'2026-09-01T00:00:00+00:00', 'Dinner', 'Manual', NULL, 0, " +
            "'2026-09-01T00:00:00+00:00', '2026-09-01T00:00:00+00:00')");
        Execute(
            "INSERT INTO Postings (Id, TransactionId, AccountId, AmountMinor, CurrencyCode) VALUES " +
            $"('{Guid.NewGuid()}', '{transactionId}', '{foodId}', 2000, 'EUR')");
        Execute(
            "INSERT INTO Postings (Id, TransactionId, AccountId, AmountMinor, CurrencyCode) VALUES " +
            $"('{Guid.NewGuid()}', '{transactionId}', '{bankId}', -2000, 'EUR')");
    }

    private void Execute(string sql)
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
