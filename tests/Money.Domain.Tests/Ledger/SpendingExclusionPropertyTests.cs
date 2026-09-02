using CsCheck;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Periods;
using Money.TestSupport;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

/// <summary>
/// Invariant I12: spending reports include only Kind=Expense accounts, so transfers, pocket
/// funding and investment purchases are structurally excluded.
/// </summary>
public sealed class SpendingExclusionPropertyTests
{
    private static readonly DateRange Whole2026 =
        DateRange.Create(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)).Value;

    [Fact]
    public void Total_spending_equals_the_sum_of_entries_on_expense_accounts_and_nothing_else()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var expected = ledger.Transactions
                .Where(t => !t.IsVoided && Whole2026.Contains(t.OccurredOn))
                .SelectMany(t => t.Postings)
                .Where(p => ledger.AccountsById[p.AccountId].Kind == AccountKind.Expense)
                .Sum(p => p.AmountMinor);

            var actual = BalanceCalculator
                .TotalSpending(Currency.Eur, ledger.AccountsById, ledger.Transactions, Whole2026)
                .AmountMinor;

            return actual == expected;
        }, iter: 2_000);
    }

    [Fact]
    public void Moving_money_into_savings_never_changes_reported_spending()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var before = BalanceCalculator
                .TotalSpending(Currency.Eur, ledger.AccountsById, ledger.Transactions, Whole2026);

            var extra = LedgerTemplates.Transfer(
                Guid.CreateVersion7(), new DateOnly(2026, 6, 15), "To savings",
                ledger.Bank, ledger.Savings,
                MoneyValue.Of(123_456, Currency.Eur),
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)).Value;

            var after = BalanceCalculator.TotalSpending(
                Currency.Eur, ledger.AccountsById,
                ledger.Transactions.Append(extra), Whole2026);

            return after == before;
        }, iter: 2_000);
    }

    [Fact]
    public void Buying_an_investment_never_changes_reported_spending()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var before = BalanceCalculator
                .TotalSpending(Currency.Eur, ledger.AccountsById, ledger.Transactions, Whole2026);

            var extra = LedgerTemplates.Transfer(
                Guid.CreateVersion7(), new DateOnly(2026, 6, 15), "Buy deposit",
                ledger.Bank, ledger.Investment,
                MoneyValue.Of(999_999, Currency.Eur),
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)).Value;

            var after = BalanceCalculator.TotalSpending(
                Currency.Eur, ledger.AccountsById,
                ledger.Transactions.Append(extra), Whole2026);

            return after == before;
        }, iter: 2_000);
    }

    [Fact]
    public void Receiving_salary_never_changes_reported_spending()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var before = BalanceCalculator
                .TotalSpending(Currency.Eur, ledger.AccountsById, ledger.Transactions, Whole2026);

            var extra = LedgerTemplates.Income(
                Guid.CreateVersion7(), new DateOnly(2026, 6, 15), "Salary", null,
                ledger.Bank, ledger.Salary,
                MoneyValue.Of(300_000, Currency.Eur),
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)).Value;

            var after = BalanceCalculator.TotalSpending(
                Currency.Eur, ledger.AccountsById,
                ledger.Transactions.Append(extra), Whole2026);

            return after == before;
        }, iter: 2_000);
    }
}
