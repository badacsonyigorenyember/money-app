using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class PostingTests
{
    [Fact]
    public void A_draft_carries_an_account_an_amount_and_an_optional_memo()
    {
        var accountId = Guid.CreateVersion7();
        var draft = new PostingDraft(accountId, MoneyValue.Of(2000, Currency.Eur), "Dinner");

        draft.AccountId.Should().Be(accountId);
        draft.Amount.AmountMinor.Should().Be(2000);
        draft.Memo.Should().Be("Dinner");
    }

    [Fact]
    public void A_draft_s_memo_is_optional()
    {
        new PostingDraft(Guid.CreateVersion7(), MoneyValue.Of(1, Currency.Eur)).Memo.Should().BeNull();
    }

    [Fact]
    public void A_posting_can_be_read_back_as_money_in_a_known_currency()
    {
        var posting = Posting.CreateForTest(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            -6000, Currency.Eur.Code, memo: null);

        posting.AmountIn(Currency.Eur).Should().Be(MoneyValue.Of(-6000, Currency.Eur));
    }

    [Fact]
    public void Reading_a_posting_in_the_wrong_currency_is_a_programmer_error()
    {
        var posting = Posting.CreateForTest(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            100, Currency.Eur.Code, memo: null);

        var act = () => posting.AmountIn(Currency.Usd);

        act.Should().Throw<CurrencyMismatchException>();
    }
}
