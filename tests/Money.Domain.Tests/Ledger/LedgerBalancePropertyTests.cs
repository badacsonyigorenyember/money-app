using CsCheck;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.TestSupport;

namespace Money.Domain.Tests.Ledger;

/// <summary>
/// Invariant I1: postings sum to zero. I3: an account's balance is the sum of its non-voided
/// postings, and equals the balance recomputed from scratch. I11: a voided transaction
/// contributes to no balance.
/// </summary>
public sealed class LedgerBalancePropertyTests
{
    [Fact]
    public void Every_generated_transaction_balances_to_zero()
    {
        LedgerGen.Ledgers.Sample(ledger =>
            ledger.Transactions.All(t => t.Postings.Sum(p => p.AmountMinor) == 0),
            iter: 2_000);
    }

    [Fact]
    public void Every_generated_transaction_has_at_least_two_entries()
    {
        LedgerGen.Ledgers.Sample(ledger =>
            ledger.Transactions.All(t => t.Postings.Count >= 2), iter: 2_000);
    }

    [Fact]
    public void An_account_balance_equals_the_sum_of_its_non_voided_entries()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            foreach (var account in ledger.Accounts)
            {
                var expected = ledger.Transactions
                    .Where(t => !t.IsVoided)
                    .SelectMany(t => t.Postings)
                    .Where(p => p.AccountId == account.Id)
                    .Sum(p => p.AmountMinor);

                var actual = BalanceCalculator
                    .BalanceOf(account.Id, Currency.Eur, ledger.Transactions).AmountMinor;

                if (actual != expected) return false;
            }

            return true;
        }, iter: 2_000);
    }

    [Fact]
    public void All_account_balances_together_sum_to_zero()
    {
        // The books balance: this is the whole-ledger form of I1.
        LedgerGen.Ledgers.Sample(ledger =>
            ledger.Accounts.Sum(a =>
                BalanceCalculator.BalanceOf(a.Id, Currency.Eur, ledger.Transactions).AmountMinor) == 0,
            iter: 2_000);
    }

    [Fact]
    public void Voiding_a_transaction_removes_exactly_its_own_contribution()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var live = ledger.Transactions.Where(t => !t.IsVoided).ToList();
            if (live.Count == 0) return true;

            var target = live[0];
            var before = BalanceCalculator.BalanceOf(ledger.Bank.Id, Currency.Eur, ledger.Transactions);
            var contribution = target.Postings.Where(p => p.AccountId == ledger.Bank.Id)
                                              .Sum(p => p.AmountMinor);

            target.Void("Property test", new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

            var after = BalanceCalculator.BalanceOf(ledger.Bank.Id, Currency.Eur, ledger.Transactions);

            return after.AmountMinor == before.AmountMinor - contribution;
        }, iter: 2_000);
    }
}
