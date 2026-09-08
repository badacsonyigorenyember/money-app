using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class LedgerTemplatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static Account Root(string name, AccountKind kind, AccountRole role) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, null, Currency.Eur, Now).Value;

    private static MoneyValue Eur(long minor) => MoneyValue.Of(minor, Currency.Eur);

    private readonly Account _bank = Root("Current", AccountKind.Asset, AccountRole.Bank);
    private readonly Account _savings = Root("Rainy day", AccountKind.Asset, AccountRole.SavingsPocket);
    private readonly Account _food = Root("Food", AccountKind.Expense, AccountRole.Category);
    private readonly Account _salary = Root("Salary", AccountKind.Income, AccountRole.Category);
    private readonly Account _opening = Root("Opening balance", AccountKind.Equity, AccountRole.OpeningBalance);
    private readonly Account _unclassifiedExpense = Root("Unclassified", AccountKind.Expense, AccountRole.Category);
    private readonly Account _unclassifiedIncome = Root("Unclassified income", AccountKind.Income, AccountRole.Category);

    [Fact]
    public void An_expense_debits_the_category_and_credits_the_account()
    {
        var transaction = LedgerTemplates.Expense(
            Guid.CreateVersion7(Now), Today, "Dinner", "Trattoria",
            _bank, _food, Eur(2000), Now).Value;

        transaction.Postings.Single(p => p.AccountId == _food.Id).AmountMinor.Should().Be(2000);
        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(-2000);
        transaction.Payee.Should().Be("Trattoria");
    }

    [Fact]
    public void Income_debits_the_account_and_credits_the_income_category()
    {
        var transaction = LedgerTemplates.Income(
            Guid.CreateVersion7(Now), Today, "September salary", "Employer",
            _bank, _salary, Eur(300_000), Now).Value;

        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(300_000);
        transaction.Postings.Single(p => p.AccountId == _salary.Id).AmountMinor.Should().Be(-300_000);
    }

    [Fact]
    public void A_transfer_moves_money_between_two_asset_accounts_and_touches_no_category()
    {
        var transaction = LedgerTemplates.Transfer(
            Guid.CreateVersion7(Now), Today, "To savings", _bank, _savings, Eur(50_000), Now).Value;

        transaction.Postings.Single(p => p.AccountId == _savings.Id).AmountMinor.Should().Be(50_000);
        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(-50_000);
        transaction.Postings.Should().HaveCount(2);
    }

    [Fact]
    public void An_opening_balance_debits_the_account_and_credits_equity()
    {
        var transaction = LedgerTemplates.OpeningBalance(
            Guid.CreateVersion7(Now), Today, _bank, _opening, Eur(100_000), Now).Value;

        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(100_000);
        transaction.Postings.Single(p => p.AccountId == _opening.Id).AmountMinor.Should().Be(-100_000);
        transaction.Description.Should().Be("Opening balance");
    }

    [Fact]
    public void An_expense_against_something_that_is_not_a_category_is_rejected()
    {
        LedgerTemplates.Expense(Guid.CreateVersion7(Now), Today, "Dinner", null,
                                _bank, _savings, Eur(2000), Now)
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public void An_expense_against_an_income_category_is_rejected()
    {
        LedgerTemplates.Expense(Guid.CreateVersion7(Now), Today, "Dinner", null,
                                _bank, _salary, Eur(2000), Now)
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public void Income_against_an_expense_category_is_rejected()
    {
        // Mirrors the two Expense rejection cases above: _food has Kind=Expense but IS a
        // category (IsCategory=true), so this is the case that distinguishes the Kind check
        // from the IsCategory check rather than letting one subsume the other.
        LedgerTemplates.Income(Guid.CreateVersion7(Now), Today, "Salary", null,
                               _bank, _food, Eur(300_000), Now)
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public void A_transfer_into_a_category_is_rejected()
    {
        LedgerTemplates.Transfer(Guid.CreateVersion7(Now), Today, "Oops", _bank, _food, Eur(100), Now)
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public void A_negative_or_zero_amount_is_rejected_by_every_template()
    {
        LedgerTemplates.Expense(Guid.CreateVersion7(Now), Today, "Dinner", null,
                                _bank, _food, Eur(0), Now)
            .Error!.Code.Should().Be("transaction.zero_amount");

        LedgerTemplates.Transfer(Guid.CreateVersion7(Now), Today, "To savings",
                                 _bank, _savings, Eur(-1), Now)
            .Error!.Code.Should().Be("transaction.zero_amount");
    }

    [Fact]
    public void Expense_throws_for_any_null_argument()
    {
        var actNullPaidFrom = () => LedgerTemplates.Expense(
            Guid.CreateVersion7(Now), Today, "Dinner", null, null!, _food, Eur(2000), Now);
        var actNullCategory = () => LedgerTemplates.Expense(
            Guid.CreateVersion7(Now), Today, "Dinner", null, _bank, null!, Eur(2000), Now);
        var actNullAmount = () => LedgerTemplates.Expense(
            Guid.CreateVersion7(Now), Today, "Dinner", null, _bank, _food, null!, Now);

        actNullPaidFrom.Should().Throw<ArgumentNullException>();
        actNullCategory.Should().Throw<ArgumentNullException>();
        actNullAmount.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Income_throws_for_any_null_argument()
    {
        var actNullReceivedInto = () => LedgerTemplates.Income(
            Guid.CreateVersion7(Now), Today, "Salary", null, null!, _salary, Eur(300_000), Now);
        var actNullCategory = () => LedgerTemplates.Income(
            Guid.CreateVersion7(Now), Today, "Salary", null, _bank, null!, Eur(300_000), Now);
        var actNullAmount = () => LedgerTemplates.Income(
            Guid.CreateVersion7(Now), Today, "Salary", null, _bank, _salary, null!, Now);

        actNullReceivedInto.Should().Throw<ArgumentNullException>();
        actNullCategory.Should().Throw<ArgumentNullException>();
        actNullAmount.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Transfer_throws_for_any_null_argument()
    {
        var actNullFrom = () => LedgerTemplates.Transfer(
            Guid.CreateVersion7(Now), Today, "To savings", null!, _savings, Eur(50_000), Now);
        var actNullTo = () => LedgerTemplates.Transfer(
            Guid.CreateVersion7(Now), Today, "To savings", _bank, null!, Eur(50_000), Now);
        var actNullAmount = () => LedgerTemplates.Transfer(
            Guid.CreateVersion7(Now), Today, "To savings", _bank, _savings, null!, Now);

        actNullFrom.Should().Throw<ArgumentNullException>();
        actNullTo.Should().Throw<ArgumentNullException>();
        actNullAmount.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OpeningBalance_throws_for_any_null_argument()
    {
        var actNullAccount = () => LedgerTemplates.OpeningBalance(
            Guid.CreateVersion7(Now), Today, null!, _opening, Eur(100_000), Now);
        var actNullEquity = () => LedgerTemplates.OpeningBalance(
            Guid.CreateVersion7(Now), Today, _bank, null!, Eur(100_000), Now);
        var actNullAmount = () => LedgerTemplates.OpeningBalance(
            Guid.CreateVersion7(Now), Today, _bank, _opening, null!, Now);

        actNullAccount.Should().Throw<ArgumentNullException>();
        actNullEquity.Should().Throw<ArgumentNullException>();
        actNullAmount.Should().Throw<ArgumentNullException>();
    }

    // --- Import (bank feed) -------------------------------------------------------------
    //
    // The signed amount is stated from the bank account's point of view in the ledger's own
    // convention: positive = debit = money in, negative = money out. Getting this backwards is
    // the single most damaging bug an import can have, so both directions are pinned here.

    [Fact]
    public void An_imported_credit_debits_the_bank_account_and_credits_unclassified_income()
    {
        var transaction = LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "Salary OTP", "Employer Kft",
            _bank, _unclassifiedExpense, _unclassifiedIncome, Eur(300_000), "eb:abc", Now).Value;

        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(300_000);
        transaction.Postings.Single(p => p.AccountId == _unclassifiedIncome.Id)
                   .AmountMinor.Should().Be(-300_000);
        transaction.Postings.Should().HaveCount(2);
    }

    [Fact]
    public void An_imported_debit_debits_unclassified_expense_and_credits_the_bank_account()
    {
        var transaction = LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR 1234", "SPAR",
            _bank, _unclassifiedExpense, _unclassifiedIncome, Eur(-2_000), "eb:def", Now).Value;

        transaction.Postings.Single(p => p.AccountId == _unclassifiedExpense.Id)
                   .AmountMinor.Should().Be(2_000);
        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(-2_000);
        transaction.Postings.Should().HaveCount(2);
    }

    [Fact]
    public void An_import_carries_its_source_kind_and_external_reference()
    {
        var transaction = LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR 1234", null,
            _bank, _unclassifiedExpense, _unclassifiedIncome, Eur(-2_000), "  eb:def  ", Now).Value;

        transaction.SourceKind.Should().Be(TransactionSourceKind.Import);
        transaction.ExternalRef.Should().Be("eb:def");
    }

    [Fact]
    public void An_import_without_an_external_reference_is_rejected()
    {
        // The external reference is the dedup key. Without one, re-running the sync would
        // duplicate the line, so an import that cannot be identified must not be built at all.
        LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR 1234", null,
            _bank, _unclassifiedExpense, _unclassifiedIncome, Eur(-2_000), "   ", Now)
            .Error!.Code.Should().Be("transaction.external_ref_required");
    }

    [Fact]
    public void An_imported_zero_amount_is_rejected()
    {
        LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "Nothing", null,
            _bank, _unclassifiedExpense, _unclassifiedIncome, Eur(0), "eb:zero", Now)
            .Error!.Code.Should().Be("transaction.zero_amount");
    }

    [Fact]
    public void An_import_into_something_that_is_not_an_asset_account_is_rejected()
    {
        LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "Oops", null,
            _food, _unclassifiedExpense, _unclassifiedIncome, Eur(-2_000), "eb:oops", Now)
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public void An_import_whose_unclassified_counterparties_are_the_wrong_kind_is_rejected()
    {
        // Swapped: the expense slot is handed an Income category and vice versa. Both directions
        // are checked up front, not only the one this particular amount's sign would reach.
        LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR", null,
            _bank, _unclassifiedIncome, _unclassifiedExpense, Eur(-2_000), "eb:swap", Now)
            .Error!.Code.Should().Be("account.not_a_category");

        LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "Salary", null,
            _bank, _unclassifiedIncome, _unclassifiedExpense, Eur(300_000), "eb:swap2", Now)
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public void An_import_against_a_non_category_counterparty_is_rejected()
    {
        LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR", null,
            _bank, _savings, _unclassifiedIncome, Eur(-2_000), "eb:nc", Now)
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public void Imported_throws_for_any_null_argument()
    {
        var actNullBank = () => LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR", null,
            null!, _unclassifiedExpense, _unclassifiedIncome, Eur(-2_000), "eb:n", Now);
        var actNullExpense = () => LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR", null,
            _bank, null!, _unclassifiedIncome, Eur(-2_000), "eb:n", Now);
        var actNullIncome = () => LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR", null,
            _bank, _unclassifiedExpense, null!, Eur(-2_000), "eb:n", Now);
        var actNullAmount = () => LedgerTemplates.Imported(
            Guid.CreateVersion7(Now), Today, "SPAR", null,
            _bank, _unclassifiedExpense, _unclassifiedIncome, null!, "eb:n", Now);

        actNullBank.Should().Throw<ArgumentNullException>();
        actNullExpense.Should().Throw<ArgumentNullException>();
        actNullIncome.Should().Throw<ArgumentNullException>();
        actNullAmount.Should().Throw<ArgumentNullException>();
    }
}
