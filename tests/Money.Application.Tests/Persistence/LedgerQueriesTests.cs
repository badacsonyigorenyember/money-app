using CsCheck;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Periods;
using Money.Infrastructure.Persistence;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

public sealed class LedgerQueriesTests
{
    // CsCheck 4.2.0's Gen<T> has no Take(n): each Single() call draws one fresh sample, so this
    // draws `count` independent generated ledgers - the same shape the brief's Take(25) intended.
    private static IEnumerable<LedgerGen.GeneratedLedger> SampleLedgers(int count) =>
        Enumerable.Range(0, count).Select(_ => LedgerGen.Ledgers.Single());

    private static async Task<(MoneyDbContext Context, LedgerQueries Queries, LedgerGen.GeneratedLedger Ledger)>
        SeedAsync(SqliteFixture fixture, LedgerGen.GeneratedLedger ledger)
    {
        var context = fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, ledger);
        return (context, new LedgerQueries(context), ledger);
    }

    [Fact]
    public async Task An_account_balance_from_sql_matches_the_domain_calculator()
    {
        foreach (var ledger in SampleLedgers(25))
        {
            using var fixture = new SqliteFixture();
            var (context, queries, seeded) = await SeedAsync(fixture, ledger);

            using (context)
            {
                foreach (var account in seeded.Accounts)
                {
                    var fromSql = await queries.BalanceOfAsync(account.Id, null);
                    var fromDomain = BalanceCalculator
                        .BalanceOf(account.Id, Currency.Eur, seeded.Transactions).AmountMinor;

                    fromSql.Should().Be(fromDomain, "account {0}", account.Path);
                }
            }
        }
    }

    [Fact]
    public async Task A_balance_as_of_a_date_from_sql_matches_the_domain_calculator()
    {
        var asOf = new DateOnly(2026, 6, 30);

        foreach (var ledger in SampleLedgers(25))
        {
            using var fixture = new SqliteFixture();
            var (context, queries, seeded) = await SeedAsync(fixture, ledger);

            using (context)
            {
                var fromSql = await queries.BalanceOfAsync(seeded.Bank.Id, asOf);
                var fromDomain = BalanceCalculator
                    .BalanceOf(seeded.Bank.Id, Currency.Eur, seeded.Transactions, asOf).AmountMinor;

                fromSql.Should().Be(fromDomain);
            }
        }
    }

    [Fact]
    public async Task A_subtree_balance_from_sql_matches_the_domain_calculator()
    {
        foreach (var ledger in SampleLedgers(25))
        {
            using var fixture = new SqliteFixture();
            var (context, queries, seeded) = await SeedAsync(fixture, ledger);

            using (context)
            {
                var fromSql = await queries.SubtreeBalanceAsync(seeded.Groceries.Path, null, null);
                var fromDomain = BalanceCalculator
                    .SubtreeBalance(seeded.Groceries, Currency.Eur, seeded.AccountsById, seeded.Transactions)
                    .AmountMinor;

                fromSql.Should().Be(fromDomain);
            }
        }
    }

    [Fact]
    public async Task A_windowed_subtree_balance_from_sql_matches_the_domain_calculator()
    {
        var window = DateRange.Create(new DateOnly(2026, 3, 1), new DateOnly(2026, 6, 1)).Value;

        foreach (var ledger in SampleLedgers(25))
        {
            using var fixture = new SqliteFixture();
            var (context, queries, seeded) = await SeedAsync(fixture, ledger);

            using (context)
            {
                var fromSql = await queries.SubtreeBalanceAsync(
                    seeded.Groceries.Path, window.Start, window.EndExclusive);
                var fromDomain = BalanceCalculator
                    .SubtreeBalance(seeded.Groceries, Currency.Eur, seeded.AccountsById,
                                    seeded.Transactions, window).AmountMinor;

                fromSql.Should().Be(fromDomain);
            }
        }
    }

    [Fact]
    public async Task A_subtree_balance_does_not_pick_up_a_sibling_with_a_similar_name()
    {
        // "/expense/groceries" must not match "/expense/groceries-online".
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        var queries = new LedgerQueries(context);
        var groceriesOnly = await queries.SubtreeBalanceAsync(ledger.Groceries.Path, null, null);
        var expected = BalanceCalculator
            .SubtreeBalance(ledger.Groceries, Currency.Eur, ledger.AccountsById, ledger.Transactions)
            .AmountMinor;

        groceriesOnly.Should().Be(expected);
    }

    [Fact]
    public async Task The_transaction_list_pages_forward_without_repeating_or_skipping()
    {
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        var queries = new LedgerQueries(context);
        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var page = await queries.ListAsync(new TransactionQuery(
                null, null, null, null, null, IncludeVoided: true, cursor, Limit: 7));

            seen.AddRange(page.Rows.Select(r => r.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Should().OnlyHaveUniqueItems();
        seen.Should().HaveCount(ledger.Transactions.Count);
    }

    [Fact]
    public async Task The_transaction_list_returns_newest_first()
    {
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var page = await new LedgerQueries(context).ListAsync(new TransactionQuery(
            null, null, null, null, null, IncludeVoided: true, null, Limit: 50));

        page.Rows.Select(r => r.OccurredOn).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task The_transaction_list_can_hide_voided_rows_and_filter_by_text_and_date()
    {
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);
        var queries = new LedgerQueries(context);

        var live = await queries.ListAsync(new TransactionQuery(
            null, null, null, null, null, IncludeVoided: false, null, 200));
        live.Rows.Should().OnlyContain(r => !r.IsVoided);

        var opening = await queries.ListAsync(new TransactionQuery(
            null, null, null, null, "Opening", IncludeVoided: true, null, 200));
        opening.Rows.Should().OnlyContain(r => r.Description.Contains("Opening"));

        var firstQuarter = await queries.ListAsync(new TransactionQuery(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null, null, null, true, null, 200));
        firstQuarter.Rows.Should().OnlyContain(r =>
            r.OccurredOn >= new DateOnly(2026, 1, 1) && r.OccurredOn <= new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task The_transaction_list_can_filter_to_one_account()
    {
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        var page = await new LedgerQueries(context).ListAsync(new TransactionQuery(
            null, null, ledger.Savings.Id, null, null, IncludeVoided: true, null, 200));

        var expected = ledger.Transactions
            .Count(t => t.Postings.Any(p => p.AccountId == ledger.Savings.Id));

        page.Rows.Should().HaveCount(expected);
    }
}
