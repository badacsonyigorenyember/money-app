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

    private readonly Account _savings = Account.Create(
        Guid.CreateVersion7(Now), "Rainy day", AccountKind.Asset, AccountRole.SavingsPocket, null, Currency.Eur, Now).Value;

    private readonly Account _investment = Account.Create(
        Guid.CreateVersion7(Now), "Term deposit", AccountKind.Asset, AccountRole.Investment, null, Currency.Eur, Now).Value;

    private readonly Account _salary = Account.Create(
        Guid.CreateVersion7(Now), "Salary", AccountKind.Income, AccountRole.Category, null, Currency.Eur, Now).Value;

    private Account _steam = null!;

    private IReadOnlyDictionary<Guid, Account> Accounts =>
        new[] { _bank, _gaming, _savings, _investment, _salary, _steam }.ToDictionary(a => a.Id);

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

    /// <summary>Moves money between the bank and another asset account: a savings-pocket
    /// transfer or an investment purchase, depending on <paramref name="to"/>. Both sides are
    /// Kind=Asset, so neither should ever be counted as spending.</summary>
    private Transaction Transfer(Account to, long minor, DateOnly on) =>
        Transaction.Create(Guid.CreateVersion7(Now), on, "Transfer", null,
                           TransactionSourceKind.Manual, null,
                           [new PostingDraft(to.Id, Eur(minor)), new PostingDraft(_bank.Id, Eur(-minor))],
                           Accounts, Now).Value;

    /// <summary>Salary: Kind=Income, Role=Category - the same Role as an Expense category, so
    /// only Kind tells the two apart.</summary>
    private Transaction Income(long minor, DateOnly on) =>
        Transaction.Create(Guid.CreateVersion7(Now), on, "Salary", null,
                           TransactionSourceKind.Manual, null,
                           [new PostingDraft(_bank.Id, Eur(minor)), new PostingDraft(_salary.Id, Eur(-minor))],
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

    private static readonly DateRange September =
        DateRange.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)).Value;

    [Fact]
    public void Total_spending_counts_the_expense_and_excludes_a_transfer_between_asset_accounts()
    {
        // The I12 case in miniature: a transfer between two Kind=Asset accounts must contribute
        // nothing to spending, no matter how large, while an ordinary expense still counts.
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 9, 5)),
            Transfer(_savings, 5_000_000, new DateOnly(2026, 9, 6))
        };

        BalanceCalculator.TotalSpending(Currency.Eur, Accounts, transactions, September)
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void Total_spending_excludes_an_investment_purchase()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1500, new DateOnly(2026, 9, 5)),
            Transfer(_investment, 7_000_000, new DateOnly(2026, 9, 10))
        };

        BalanceCalculator.TotalSpending(Currency.Eur, Accounts, transactions, September)
            .Should().Be(Eur(1500));
    }

    [Fact]
    public void Total_spending_excludes_income()
    {
        // Income shares Role=Category with Expense - both are "categories" in the account tree.
        // Only Kind tells them apart, so this is the case a Role-based filter would get wrong.
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 9, 5)),
            Income(300_000, new DateOnly(2026, 9, 1))
        };

        BalanceCalculator.TotalSpending(Currency.Eur, Accounts, transactions, September)
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void Total_spending_excludes_a_voided_expense()
    {
        var kept = Spend(_gaming, 1200, new DateOnly(2026, 9, 3));
        var voided = Spend(_steam, 9999, new DateOnly(2026, 9, 4));
        voided.Void("Mistake", Now);

        BalanceCalculator.TotalSpending(Currency.Eur, Accounts, new[] { kept, voided }, September)
            .Should().Be(Eur(1200));
    }

    [Fact]
    public void Total_spending_window_is_half_open_start_inclusive_end_exclusive()
    {
        var onStart = Spend(_gaming, 1000, new DateOnly(2026, 9, 1));
        var onEndExclusive = Spend(_gaming, 2000, new DateOnly(2026, 10, 1));

        BalanceCalculator.TotalSpending(Currency.Eur, Accounts, new[] { onStart, onEndExclusive }, September)
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void A_posting_in_a_different_currency_is_excluded_rather_than_summed_in()
    {
        // BalanceCalculator's currency filter matters even though a single account only ever
        // holds postings in its own currency (Transaction.BuildPostings enforces that): a caller
        // can still ask for the wrong currency. Removing the filter would silently sum the raw
        // minor units into a Money value mislabelled with the requested currency instead of
        // returning zero.
        var eurBank = Account.Create(Guid.CreateVersion7(Now), "EUR Bank", AccountKind.Asset,
                                     AccountRole.Bank, null, Currency.Eur, Now).Value;
        var eurFood = Account.Create(Guid.CreateVersion7(Now), "Food", AccountKind.Expense,
                                     AccountRole.Category, null, Currency.Eur, Now).Value;
        var usdBank = Account.Create(Guid.CreateVersion7(Now), "USD Bank", AccountKind.Asset,
                                     AccountRole.Bank, null, Currency.Usd, Now).Value;
        var usdSavings = Account.Create(Guid.CreateVersion7(Now), "USD Savings", AccountKind.Asset,
                                        AccountRole.SavingsPocket, null, Currency.Usd, Now).Value;
        var lookup = new[] { eurBank, eurFood, usdBank, usdSavings }.ToDictionary(a => a.Id);

        var transaction = Transaction.Create(
            Guid.CreateVersion7(Now), new DateOnly(2026, 9, 1), "Mixed currency batch", null,
            TransactionSourceKind.Manual, null,
            [
                new PostingDraft(eurFood.Id, Eur(2000)),
                new PostingDraft(eurBank.Id, Eur(-2000)),
                new PostingDraft(usdSavings.Id, MoneyValue.Of(5000, Currency.Usd)),
                new PostingDraft(usdBank.Id, MoneyValue.Of(-5000, Currency.Usd))
            ],
            lookup, Now).Value;

        BalanceCalculator.BalanceOf(usdBank.Id, Currency.Eur, new[] { transaction })
            .Should().Be(Eur(0));
    }

    [Fact]
    public void IsInSubtree_throws_for_a_null_candidate_or_a_null_root()
    {
        var actNullCandidate = () => BalanceCalculator.IsInSubtree(null!, _gaming);
        var actNullRoot = () => BalanceCalculator.IsInSubtree(_gaming, null!);

        actNullCandidate.Should().Throw<ArgumentNullException>();
        actNullRoot.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BalanceOf_throws_for_a_null_currency_or_a_null_transaction_sequence()
    {
        // A non-empty transaction touching the queried account is deliberate: with an empty
        // list, removing the currency guard is masked by MoneyValue.Of's own null check at the
        // very end (same exception type, different call site). A real posting forces the method
        // to dereference currency.Code inside the loop, where a missing guard surfaces as a
        // NullReferenceException instead - genuinely distinguishing the two.
        var transactions = new[] { Spend(_gaming, 1000, new DateOnly(2026, 9, 1)) };

        var actNullCurrency = () => BalanceCalculator.BalanceOf(_bank.Id, null!, transactions);
        var actNullTransactions = () => BalanceCalculator.BalanceOf(_bank.Id, Currency.Eur, null!);

        actNullCurrency.Should().Throw<ArgumentNullException>();
        actNullTransactions.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SubtreeBalance_throws_for_any_null_argument()
    {
        var transactions = new[] { Spend(_gaming, 1000, new DateOnly(2026, 9, 1)) };

        // An empty lookup, not Accounts, for the null-root case: with a populated lookup, a
        // posting resolves to a real account and IsInSubtree's own null check on root fires
        // first, masking a missing guard here behind the same exception type. With nothing to
        // resolve, IsInSubtree is never reached, so only this method's own guard can explain an
        // exception - which is exactly what should distinguish "guard present" from "removed".
        var actNullRoot = () => BalanceCalculator.SubtreeBalance(
            null!, Currency.Eur, new Dictionary<Guid, Account>(), transactions);
        var actNullCurrency = () => BalanceCalculator.SubtreeBalance(_gaming, null!, Accounts, transactions);
        var actNullAccounts = () => BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, null!, transactions);
        var actNullTransactions = () => BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, Accounts, null!);

        actNullRoot.Should().Throw<ArgumentNullException>();
        actNullCurrency.Should().Throw<ArgumentNullException>();
        actNullAccounts.Should().Throw<ArgumentNullException>();
        actNullTransactions.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TotalSpending_throws_for_any_null_argument()
    {
        var transactions = new[] { Spend(_gaming, 1000, new DateOnly(2026, 9, 5)) };

        var actNullCurrency = () => BalanceCalculator.TotalSpending(null!, Accounts, transactions, September);
        var actNullAccounts = () => BalanceCalculator.TotalSpending(Currency.Eur, null!, transactions, September);
        var actNullTransactions = () => BalanceCalculator.TotalSpending(Currency.Eur, Accounts, null!, September);

        actNullCurrency.Should().Throw<ArgumentNullException>();
        actNullAccounts.Should().Throw<ArgumentNullException>();
        actNullTransactions.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_voided_transaction_is_excluded_from_a_subtree_balance()
    {
        var kept = Spend(_gaming, 1000, new DateOnly(2026, 9, 1));
        var voided = Spend(_gaming, 9999, new DateOnly(2026, 9, 2));
        voided.Void("Mistake", Now);

        BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, Accounts, new[] { kept, voided })
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void A_subtree_balance_excludes_postings_in_a_different_currency_than_queried()
    {
        var transactions = new[] { Spend(_gaming, 1000, new DateOnly(2026, 9, 1)) };

        BalanceCalculator.SubtreeBalance(_gaming, Currency.Usd, Accounts, transactions)
            .Should().Be(MoneyValue.Of(0, Currency.Usd));
    }

    [Fact]
    public void A_subtree_balance_ignores_a_posting_whose_account_is_missing_from_the_lookup()
    {
        // accountsById can be a partial view of the tree (e.g. one page of accounts). A posting
        // for an account outside that view must be skipped, not crash the calculation.
        var transactions = new[] { Spend(_gaming, 1000, new DateOnly(2026, 9, 1)) };
        var incompleteLookup = new[] { _gaming }.ToDictionary(a => a.Id);

        BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, incompleteLookup, transactions)
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void Total_spending_excludes_postings_in_a_different_currency_than_queried()
    {
        var transactions = new[] { Spend(_gaming, 1000, new DateOnly(2026, 9, 5)) };

        BalanceCalculator.TotalSpending(Currency.Usd, Accounts, transactions, September)
            .Should().Be(MoneyValue.Of(0, Currency.Usd));
    }

    [Fact]
    public void Total_spending_ignores_a_posting_whose_account_is_missing_from_the_lookup()
    {
        var transactions = new[] { Spend(_gaming, 1000, new DateOnly(2026, 9, 5)) };
        var incompleteLookup = new[] { _gaming }.ToDictionary(a => a.Id);

        BalanceCalculator.TotalSpending(Currency.Eur, incompleteLookup, transactions, September)
            .Should().Be(Eur(1000));
    }

    // The three tests below pin genuine money-safety behaviour, not just line coverage: every
    // running total in BalanceCalculator is summed inside `checked(...)`. Silently wrapping past
    // long.MaxValue instead of throwing would be a real hole in the money logic (a huge balance
    // could wrap around to a small or negative number without any error), so each test forces an
    // overflow across two legitimately-balanced transactions and asserts it surfaces as an
    // OverflowException rather than a wrong-but-plausible balance.
    private const long NearMax = 9_000_000_000_000_000_000L; // just under long.MaxValue / 2 * 2 overflows

    [Fact]
    public void BalanceOf_throws_on_overflow_instead_of_silently_wrapping()
    {
        var vault = Account.Create(Guid.CreateVersion7(Now), "Vault", AccountKind.Asset,
                                   AccountRole.Cash, null, Currency.Eur, Now).Value;
        var lookup = new[] { _bank, vault }.ToDictionary(a => a.Id);

        var first = Transaction.Create(Guid.CreateVersion7(Now), new DateOnly(2026, 1, 1), "Huge", null,
            TransactionSourceKind.Manual, null,
            [new PostingDraft(_bank.Id, Eur(NearMax)), new PostingDraft(vault.Id, Eur(-NearMax))],
            lookup, Now).Value;
        var second = Transaction.Create(Guid.CreateVersion7(Now), new DateOnly(2026, 1, 2), "Huge again", null,
            TransactionSourceKind.Manual, null,
            [new PostingDraft(_bank.Id, Eur(NearMax)), new PostingDraft(vault.Id, Eur(-NearMax))],
            lookup, Now).Value;

        var act = () => BalanceCalculator.BalanceOf(_bank.Id, Currency.Eur, [first, second]);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void SubtreeBalance_throws_on_overflow_instead_of_silently_wrapping()
    {
        var lookup = new[] { _bank, _gaming, _steam }.ToDictionary(a => a.Id);

        var onParent = Transaction.Create(Guid.CreateVersion7(Now), new DateOnly(2026, 1, 1), "Huge", null,
            TransactionSourceKind.Manual, null,
            [new PostingDraft(_gaming.Id, Eur(NearMax)), new PostingDraft(_bank.Id, Eur(-NearMax))],
            lookup, Now).Value;
        var onChild = Transaction.Create(Guid.CreateVersion7(Now), new DateOnly(2026, 1, 2), "Huge again", null,
            TransactionSourceKind.Manual, null,
            [new PostingDraft(_steam.Id, Eur(NearMax)), new PostingDraft(_bank.Id, Eur(-NearMax))],
            lookup, Now).Value;

        var act = () => BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, lookup, [onParent, onChild]);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void TotalSpending_throws_on_overflow_instead_of_silently_wrapping()
    {
        var first = Spend(_gaming, NearMax, new DateOnly(2026, 9, 5));
        var second = Spend(_steam, NearMax, new DateOnly(2026, 9, 6));

        var act = () => BalanceCalculator.TotalSpending(Currency.Eur, Accounts, [first, second], September);

        act.Should().Throw<OverflowException>();
    }
}
