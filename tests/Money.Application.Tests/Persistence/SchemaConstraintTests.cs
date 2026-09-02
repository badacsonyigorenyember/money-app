using Microsoft.Data.Sqlite;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

/// <summary>
/// Proves the *database*, not the domain, refuses bad data. Every test here executes raw SQL
/// against a real SQLite connection with migrations applied - a test that only exercises the
/// domain's own validation proves nothing about the schema.
///
/// Kind, Role and SourceKind are persisted as their integer enum values (spec D9 / the
/// "storage rules" in the task brief), so this file writes numeric literals for those columns
/// rather than the enum names.
/// </summary>
public sealed class SchemaConstraintTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private int ExecuteRaw(string sql)
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    [Fact]
    public void The_schema_has_the_expected_tables()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        using var reader = command.ExecuteReader();

        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));

        tables.Should().Contain(["Accounts", "Transactions", "Postings", "Settings", "IdempotencyRecords"]);
    }

    [Fact]
    public void A_posting_of_zero_is_refused_by_the_database()
    {
        SeedOneAccountAndTransaction(out var accountId, out var transactionId);

        var act = () => ExecuteRaw(
            $"INSERT INTO Postings (Id, TransactionId, AccountId, AmountMinor, CurrencyCode) " +
            $"VALUES ('{Guid.NewGuid()}', '{transactionId}', '{accountId}', 0, 'EUR')");

        act.Should().Throw<SqliteException>().WithMessage("*CHECK constraint failed*");
    }

    [Fact]
    public void An_account_with_an_illegal_kind_and_role_pair_is_refused_by_the_database()
    {
        var act = () => ExecuteRaw(InsertAccountSql(
            Guid.NewGuid(), "Bad", AccountKind.Asset, AccountRole.Category, "/asset/bad"));

        act.Should().Throw<SqliteException>().WithMessage("*CHECK constraint failed*");
    }

    [Fact]
    public void Two_accounts_cannot_share_a_path()
    {
        ExecuteRaw(InsertAccountSql(
            Guid.NewGuid(), "Food", AccountKind.Expense, AccountRole.Category, "/expense/food"));

        var act = () => ExecuteRaw(InsertAccountSql(
            Guid.NewGuid(), "Food again", AccountKind.Expense, AccountRole.Category, "/expense/food"));

        act.Should().Throw<SqliteException>().WithMessage("*UNIQUE constraint failed*");
    }

    [Fact]
    public void Two_recurring_transactions_from_the_same_rule_on_the_same_day_are_refused()
    {
        // This is the idempotency guard phase 3 depends on. It must exist and work before
        // any recurring code is written.
        var ruleId = Guid.NewGuid();
        ExecuteRaw(InsertTransactionSql(
            Guid.NewGuid(), "2026-09-01", "Rent", TransactionSourceKind.Recurring, ruleId));

        var act = () => ExecuteRaw(InsertTransactionSql(
            Guid.NewGuid(), "2026-09-01", "Rent again", TransactionSourceKind.Recurring, ruleId));

        act.Should().Throw<SqliteException>().WithMessage("*UNIQUE constraint failed*");
    }

    [Fact]
    public void Two_manual_transactions_on_the_same_day_are_allowed()
    {
        // The idempotency index is partial: it must not stop the user entering two coffees.
        ExecuteRaw(InsertTransactionSql(
            Guid.NewGuid(), "2026-09-01", "Coffee", TransactionSourceKind.Manual, null));
        var act = () => ExecuteRaw(InsertTransactionSql(
            Guid.NewGuid(), "2026-09-01", "Coffee again", TransactionSourceKind.Manual, null));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_posting_pointing_at_no_transaction_is_refused()
    {
        var act = () => ExecuteRaw(
            "INSERT INTO Postings (Id, TransactionId, AccountId, AmountMinor, CurrencyCode) VALUES " +
            $"('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', 100, 'EUR')");

        act.Should().Throw<SqliteException>().WithMessage("*FOREIGN KEY constraint failed*");
    }

    [Fact]
    public void No_column_in_the_schema_uses_the_real_type()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText =
            "SELECT m.name, p.name, p.type FROM sqlite_master m " +
            "JOIN pragma_table_info(m.name) p WHERE m.type = 'table'";
        using var reader = command.ExecuteReader();

        var offenders = new List<string>();
        while (reader.Read())
        {
            if (reader.GetString(2).Contains("REAL", StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        offenders.Should().BeEmpty("spec D9: money and rates never touch REAL");
    }

    private void SeedOneAccountAndTransaction(out Guid accountId, out Guid transactionId)
    {
        accountId = Guid.NewGuid();
        transactionId = Guid.NewGuid();
        ExecuteRaw(InsertAccountSql(
            accountId, "Food", AccountKind.Expense, AccountRole.Category, "/expense/food"));
        ExecuteRaw(InsertTransactionSql(
            transactionId, "2026-09-01", "Dinner", TransactionSourceKind.Manual, null));
    }

    private static string InsertAccountSql(
        Guid id, string name, AccountKind kind, AccountRole role, string path) =>
        "INSERT INTO Accounts (Id, Name, Kind, Role, Path, CurrencyCode, IsArchived, SortOrder, " +
        $"CreatedAtUtc, UpdatedAtUtc) VALUES ('{id}', '{name}', {(int)kind}, {(int)role}, '{path}', 'EUR', 0, 0, " +
        "'2026-09-01T00:00:00+00:00', '2026-09-01T00:00:00+00:00')";

    private static string InsertTransactionSql(
        Guid id, string occurredOn, string description, TransactionSourceKind sourceKind, Guid? sourceId) =>
        "INSERT INTO Transactions (Id, OccurredOn, BookedAtUtc, Description, SourceKind, SourceId, " +
        "IsVoided, CreatedAtUtc, UpdatedAtUtc) VALUES " +
        $"('{id}', '{occurredOn}', '2026-09-01T00:00:00+00:00', '{description}', {(int)sourceKind}, " +
        $"{(sourceId is null ? "NULL" : $"'{sourceId}'")}, 0, " +
        "'2026-09-01T00:00:00+00:00', '2026-09-01T00:00:00+00:00')";
}
