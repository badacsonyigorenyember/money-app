using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class TransactionCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static Account NewAccount(
        string name, AccountKind kind, AccountRole role, Currency? currency = null) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, null, currency ?? Currency.Eur, Now).Value;

    private static Dictionary<Guid, Account> Lookup(params Account[] accounts) =>
        accounts.ToDictionary(a => a.Id);

    private static MoneyValue Eur(long minor) => MoneyValue.Of(minor, Currency.Eur);

    [Fact]
    public void A_balanced_two_sided_transaction_is_created()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", "Trattoria",
            TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now);

        result.IsSuccess.Should().BeTrue();
        var transaction = result.Value;
        transaction.Postings.Should().HaveCount(2);
        transaction.Postings.Sum(p => p.AmountMinor).Should().Be(0);
        transaction.Description.Should().Be("Dinner");
        transaction.Payee.Should().Be("Trattoria");
        transaction.BookedAtUtc.Should().Be(Now);
        transaction.CreatedAtUtc.Should().Be(Now);
        transaction.IsVoided.Should().BeFalse();
        transaction.SourceKind.Should().Be(TransactionSourceKind.Manual);
    }

    [Fact]
    public void A_split_transaction_with_three_sides_is_created()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var groceries = NewAccount("Groceries", AccountKind.Expense, AccountRole.Category);
        var alcohol = NewAccount("Alcohol", AccountKind.Expense, AccountRole.Category);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Shop", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(groceries.Id, Eur(5000)),
                new PostingDraft(alcohol.Id, Eur(1000)),
                new PostingDraft(bank.Id, Eur(-6000))
            ],
            Lookup(bank, groceries, alcohol), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Postings.Should().HaveCount(3);
    }

    [Fact]
    public void An_unbalanced_transaction_cannot_be_created()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-1900))],
            Lookup(bank, food), Now);

        result.Error!.Code.Should().Be("transaction.does_not_balance");
        result.Error.Message.Should().Contain("100");
    }

    [Fact]
    public void A_transaction_with_one_side_cannot_be_created()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Mystery", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(bank.Id, Eur(0))], Lookup(bank), Now);

        result.Error!.Code.Should().Be("transaction.too_few_entries");
    }

    [Fact]
    public void A_transaction_with_no_sides_cannot_be_created()
    {
        Transaction.Create(Guid.CreateVersion7(Now), Today, "Mystery", null,
                           TransactionSourceKind.Manual, null, [], Lookup(), Now)
            .Error!.Code.Should().Be("transaction.too_few_entries");
    }

    [Fact]
    public void A_zero_amount_entry_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Nothing", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(0)), new PostingDraft(bank.Id, Eur(0))],
            Lookup(bank, food), Now)
            .Error!.Code.Should().Be("transaction.zero_amount");
    }

    [Fact]
    public void An_entry_on_an_archived_account_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
        food.Archive(Now);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now);

        result.Error!.Code.Should().Be("transaction.account_archived");
        result.Error.Message.Should().Contain("Food");
    }

    [Fact]
    public void An_entry_on_an_unknown_account_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var ghost = Guid.CreateVersion7(Now);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(ghost, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank), Now)
            .Error!.Code.Should().Be("transaction.account_unknown");
    }

    [Fact]
    public void An_entry_whose_currency_differs_from_its_account_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(food.Id, MoneyValue.Of(2000, Currency.Usd)),
                new PostingDraft(bank.Id, MoneyValue.Of(-2000, Currency.Usd))
            ],
            Lookup(bank, food), Now)
            .Error!.Code.Should().Be("transaction.currency_mismatch_with_account");
    }

    [Fact]
    public void A_transaction_balanced_in_one_currency_but_not_another_is_rejected()
    {
        // Each posting's currency matches its own account, so this genuinely reaches the
        // per-currency GroupBy/balance check rather than tripping CurrencyMismatchWithAccount
        // first. The EUR entries balance to zero on their own; the lone USD entry does not.
        // A mutant that collapses the per-currency grouping into one global sum would still
        // reject this transaction (100 - 100 + 50 = 50 = 0 is untrue either way), but it would
        // name the wrong currency in the error - asserting the message pins the currency the
        // balance check actually blamed, not just the error code.
        var eurBank = NewAccount("Current EUR", AccountKind.Asset, AccountRole.Bank);
        var eurFood = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
        var usdBank = NewAccount("Current USD", AccountKind.Asset, AccountRole.Bank, Currency.Usd);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Mixed currencies", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(eurFood.Id, Eur(100)),
                new PostingDraft(eurBank.Id, Eur(-100)),
                new PostingDraft(usdBank.Id, MoneyValue.Of(50, Currency.Usd))
            ],
            Lookup(eurBank, eurFood, usdBank), Now);

        result.Error!.Code.Should().Be("transaction.does_not_balance");
        result.Error.Message.Should().Contain("USD");
        result.Error.Message.Should().Contain("50");
    }

    [Fact]
    public void Amounts_that_cancel_only_by_ignoring_currency_do_not_balance()
    {
        // The clearest possible demonstration that balancing is per-currency (I1), not global:
        // neither leg balances on its own, but they net to zero if currency is ignored. A mutant
        // that sums every posting into one group regardless of currency would wrongly accept
        // this as balanced.
        var eurBank = NewAccount("Current EUR", AccountKind.Asset, AccountRole.Bank);
        var usdBank = NewAccount("Current USD", AccountKind.Asset, AccountRole.Bank, Currency.Usd);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Cross-currency mixup", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(eurBank.Id, Eur(10000)),
                new PostingDraft(usdBank.Id, MoneyValue.Of(-10000, Currency.Usd))
            ],
            Lookup(eurBank, usdBank), Now);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("transaction.does_not_balance");
        result.Error.Message.Should().Contain("EUR");
    }

    [Fact]
    public void A_transaction_balanced_independently_in_two_currencies_is_created()
    {
        // Both currencies happen to balance on their own here. This guards a different mutant
        // than the two tests above: one that rejects any transaction touching more than one
        // currency outright, regardless of whether each currency actually balances.
        var eurBank = NewAccount("Current EUR", AccountKind.Asset, AccountRole.Bank);
        var eurFood = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
        var usdBank = NewAccount("Current USD", AccountKind.Asset, AccountRole.Bank, Currency.Usd);
        var usdFood = NewAccount("Food USD", AccountKind.Expense, AccountRole.Category, Currency.Usd);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Two currencies", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(eurFood.Id, Eur(100)),
                new PostingDraft(eurBank.Id, Eur(-100)),
                new PostingDraft(usdFood.Id, MoneyValue.Of(50, Currency.Usd)),
                new PostingDraft(usdBank.Id, MoneyValue.Of(-50, Currency.Usd))
            ],
            Lookup(eurBank, eurFood, usdBank, usdFood), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Postings.Should().HaveCount(4);
    }

    [Fact]
    public void The_same_account_cannot_appear_twice()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(food.Id, Eur(1000)),
                new PostingDraft(food.Id, Eur(1000)),
                new PostingDraft(bank.Id, Eur(-2000))
            ],
            Lookup(bank, food), Now)
            .Error!.Code.Should().Be("transaction.duplicate_account");
    }

    [Fact]
    public void A_blank_description_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "   ", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now)
            .Error!.Code.Should().Be("transaction.description_required");
    }

    [Fact]
    public void Every_posting_gets_the_transaction_s_id_and_a_unique_id_of_its_own()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
        var transactionId = Guid.CreateVersion7(Now);

        var transaction = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now).Value;

        transaction.Postings.Should().OnlyContain(p => p.TransactionId == transaction.Id);
        transaction.Postings.Select(p => p.Id).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void Create_throws_for_null_postings_or_a_null_account_lookup()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
        var postings = new[] { new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000)) };

        var actNullPostings = () => Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            null!, Lookup(bank, food), Now);
        var actNullAccounts = () => Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            postings, null!, Now);

        actNullPostings.Should().Throw<ArgumentNullException>();
        actNullAccounts.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_postings_collection_is_not_mutable_from_outside()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        var transaction = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now).Value;

        transaction.Postings.Should().BeAssignableTo<IReadOnlyList<Posting>>();

        // Cast explicitly and assert it succeeds before asserting IsReadOnly - a `?.` chain here
        // would make the whole assertion pass vacuously (null.Should().NotBe(false) is true) if
        // the cast ever failed, instead of proving the returned collection actually rejects
        // mutation.
        var asCollection = transaction.Postings as ICollection<Posting>;
        asCollection.Should().NotBeNull("the returned collection must expose IsReadOnly");
        asCollection!.IsReadOnly.Should().BeTrue();
    }
}
