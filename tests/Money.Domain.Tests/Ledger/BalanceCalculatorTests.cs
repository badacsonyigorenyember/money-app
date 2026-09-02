using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Periods;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class BalanceCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly Account _bank = Account.Create(
        Guid.CreateVersion7(Now), "Current", AccountKind.Asset, AccountRole.Bank, null, Currency.Eur, Now).Value;

    private readonly Account _gaming = Account.Create(
        Guid.CreateVersion7(Now), "Gaming", AccountKind.Expense, AccountRole.Category, null, Currency.Eur, Now).Value;

    private Account _steam = null!;

    private IReadOnlyDictionary<Guid, Account> Accounts =>
        new[] { _bank, _gaming, _steam }.ToDictionary(a => a.Id);

    private static MoneyValue Eur(long minor) => MoneyValue.Of(minor, Currency.Eur);

    public BalanceCalculatorTests()
    {
        _steam = Account.Create(Guid.CreateVersion7(Now), "Steam", AccountKind.Expense,
                                AccountRole.Category, _gaming, Currency.Eur, Now).Value;
    }

    private Transaction Spend(Account category, long minor, DateOnly on) =>
        Transaction.Create(Guid.CreateVersion7(Now), on, "Spend", null,
                           TransactionSourceKind.Manual, null,
                           [new PostingDraft(category.Id, Eur(minor)), new PostingDraft(_bank.Id, Eur(-minor))],
                           Accounts, Now).Value;

    [Fact]
    public void An_account_balance_is_the_sum_of_its_entries()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 9, 1)),
            Spend(_steam, 2500, new DateOnly(2026, 9, 2))
        };

        BalanceCalculator.BalanceOf(_bank.Id, Currency.Eur, transactions)
            .Should().Be(Eur(-3500));
        BalanceCalculator.BalanceOf(_gaming.Id, Currency.Eur, transactions)
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void A_balance_as_of_a_date_ignores_later_entries()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 9, 1)),
            Spend(_gaming, 2000, new DateOnly(2026, 9, 5))
        };

        BalanceCalculator.BalanceOf(_gaming.Id, Currency.Eur, transactions,
                                    asOfInclusive: new DateOnly(2026, 9, 3))
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void A_voided_transaction_contributes_nothing()
    {
        var kept = Spend(_gaming, 1000, new DateOnly(2026, 9, 1));
        var voided = Spend(_gaming, 9999, new DateOnly(2026, 9, 2));
        voided.Void("Mistake", Now);

        BalanceCalculator.BalanceOf(_gaming.Id, Currency.Eur, new[] { kept, voided })
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void A_subtree_balance_rolls_up_children()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 9, 1)),
            Spend(_steam, 2500, new DateOnly(2026, 9, 2))
        };

        BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, Accounts, transactions)
            .Should().Be(Eur(3500));
        BalanceCalculator.SubtreeBalance(_steam, Currency.Eur, Accounts, transactions)
            .Should().Be(Eur(2500));
    }

    [Fact]
    public void A_subtree_balance_can_be_windowed_to_a_period()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 8, 31)),
            Spend(_gaming, 2000, new DateOnly(2026, 9, 15)),
            Spend(_steam, 500, new DateOnly(2026, 10, 1))
        };
        var september = DateRange.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)).Value;

        BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, Accounts, transactions, september)
            .Should().Be(Eur(2000));
    }

    [Fact]
    public void An_account_is_in_its_own_subtree_but_a_lookalike_sibling_is_not()
    {
        var gamingChairs = Account.Create(Guid.CreateVersion7(Now), "Gaming chairs", AccountKind.Expense,
                                          AccountRole.Category, null, Currency.Eur, Now).Value;

        BalanceCalculator.IsInSubtree(_gaming, _gaming).Should().BeTrue();
        BalanceCalculator.IsInSubtree(_steam, _gaming).Should().BeTrue();
        BalanceCalculator.IsInSubtree(gamingChairs, _gaming).Should().BeFalse();
    }
}
