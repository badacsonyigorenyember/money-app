using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class TransactionVoidAndReplaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddDays(1);
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static Account NewAccount(string name, AccountKind kind, AccountRole role) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, null, Currency.Eur, Now).Value;

    private static MoneyValue Eur(long minor) => MoneyValue.Of(minor, Currency.Eur);

    private readonly Account _bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
    private readonly Account _food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
    private readonly Account _fun = NewAccount("Fun", AccountKind.Expense, AccountRole.Category);

    private IReadOnlyDictionary<Guid, Account> Accounts => new[] { _bank, _food, _fun }.ToDictionary(a => a.Id);

    private Transaction ADinner() => Transaction.Create(
        Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
        [new PostingDraft(_food.Id, Eur(2000)), new PostingDraft(_bank.Id, Eur(-2000))],
        Accounts, Now).Value;

    [Fact]
    public void Voiding_records_when_and_why_and_keeps_the_entries()
    {
        var transaction = ADinner();

        var result = transaction.Void("Entered twice", Later);

        result.IsSuccess.Should().BeTrue();
        transaction.IsVoided.Should().BeTrue();
        transaction.VoidedAtUtc.Should().Be(Later);
        transaction.VoidReason.Should().Be("Entered twice");
        transaction.UpdatedAtUtc.Should().Be(Later);
        transaction.Postings.Should().HaveCount(2, "voiding is not deleting");
    }

    [Fact]
    public void Voiding_twice_is_rejected()
    {
        var transaction = ADinner();
        transaction.Void("Entered twice", Later);

        transaction.Void("Again", Later).Error!.Code.Should().Be("transaction.already_voided");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Voiding_without_a_reason_is_rejected(string reason)
    {
        ADinner().Void(reason, Later).Error!.Code.Should().Be("transaction.void_reason_required");
    }

    [Fact]
    public void Replacing_swaps_the_entries_and_keeps_the_identity()
    {
        var transaction = ADinner();
        var originalId = transaction.Id;

        var result = transaction.Replace(
            new DateOnly(2026, 9, 2), "Cinema", "Cinema City",
            [new PostingDraft(_fun.Id, Eur(1500)), new PostingDraft(_bank.Id, Eur(-1500))],
            Accounts, Later);

        result.IsSuccess.Should().BeTrue();
        transaction.Id.Should().Be(originalId);
        transaction.OccurredOn.Should().Be(new DateOnly(2026, 9, 2));
        transaction.Description.Should().Be("Cinema");
        transaction.Payee.Should().Be("Cinema City");
        transaction.UpdatedAtUtc.Should().Be(Later);
        transaction.CreatedAtUtc.Should().Be(Now, "creation time never changes");
        transaction.Postings.Should().HaveCount(2);
        transaction.Postings.Should().Contain(p => p.AccountId == _fun.Id && p.AmountMinor == 1500);
        transaction.Postings.Should().NotContain(p => p.AccountId == _food.Id);
    }

    [Fact]
    public void A_replacement_that_does_not_balance_leaves_the_original_untouched()
    {
        var transaction = ADinner();

        var result = transaction.Replace(
            Today, "Broken",
            null,
            [new PostingDraft(_fun.Id, Eur(1500)), new PostingDraft(_bank.Id, Eur(-1400))],
            Accounts, Later);

        result.Error!.Code.Should().Be("transaction.does_not_balance");
        transaction.Description.Should().Be("Dinner");
        transaction.Postings.Should().Contain(p => p.AccountId == _food.Id);
        transaction.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void A_voided_transaction_cannot_be_edited()
    {
        var transaction = ADinner();
        transaction.Void("Mistake", Later);

        transaction.Replace(
            Today, "Cinema", null,
            [new PostingDraft(_fun.Id, Eur(1500)), new PostingDraft(_bank.Id, Eur(-1500))],
            Accounts, Later)
            .Error!.Code.Should().Be("transaction.cannot_edit_voided");
    }

    [Fact]
    public void Replace_throws_for_null_postings_or_a_null_account_lookup()
    {
        var transaction = ADinner();
        var postings = new[] { new PostingDraft(_fun.Id, Eur(1500)), new PostingDraft(_bank.Id, Eur(-1500)) };

        var actNullPostings = () => transaction.Replace(
            Today, "Cinema", null, null!, Accounts, Later);
        var actNullAccounts = () => transaction.Replace(
            Today, "Cinema", null, postings, null!, Later);

        actNullPostings.Should().Throw<ArgumentNullException>();
        actNullAccounts.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToString_flags_a_voided_transaction_but_not_a_live_one()
    {
        var transaction = ADinner();

        transaction.ToString().Should().NotContain("[voided]");

        transaction.Void("Mistake", Later);

        transaction.ToString().Should().Contain("[voided]");
    }

    [Fact]
    public void An_external_reference_can_be_attached_and_cleared()
    {
        var transaction = ADinner();

        transaction.SetExternalRef("receipt-1234", Later).IsSuccess.Should().BeTrue();
        transaction.ExternalRef.Should().Be("receipt-1234");

        transaction.SetExternalRef(null, Later);
        transaction.ExternalRef.Should().BeNull();
    }
}
