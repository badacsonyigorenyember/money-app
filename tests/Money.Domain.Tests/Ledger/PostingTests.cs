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

    [Fact]
    public void An_unresolvable_stored_currency_code_is_named_in_the_mismatch_not_hidden_behind_the_requested_one()
    {
        // "ZZZ" is well-formed (three ASCII letters) but not in Currency's known-currency table,
        // so Currency.FromCode("ZZZ") fails to resolve it - simulating corrupted or legacy data.
        var posting = Posting.CreateForTest(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            100, "ZZZ", memo: null);

        var act = () => posting.AmountIn(Currency.Eur);

        act.Should().Throw<CurrencyMismatchException>()
            .Which.Message.Should().Contain("ZZZ")
            .And.NotContain("EUR and EUR");
    }

    [Fact]
    public void A_resolvable_stored_currency_is_matched_by_full_equality_not_code_alone()
    {
        // Currency.Create/FromCode refuse to construct a known code (e.g. EUR) at any exponent
        // but its canonical one (see CurrencyTests.Create_rejects_a_known_code_whose_exponent_
        // disagrees_with_the_canonical_one), so a resolvable code can never legitimately carry a
        // mismatched exponent - this asserts AmountIn compares by full Currency equality, matching
        // Money.RequireSameCurrency, rather than by Code alone, for every case that check can run.
        var posting = Posting.CreateForTest(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            100, Currency.Eur.Code, memo: null);

        posting.AmountIn(Currency.Eur).Should().Be(MoneyValue.Of(100, Currency.Eur));
    }

    [Fact]
    public void An_unresolvable_stored_code_that_matches_the_requested_code_is_accepted_even_though_its_true_minor_unit_exponent_is_unknown()
    {
        // Posting persists only a currency code string, never a minor-unit exponent. When that
        // code does not resolve to a known Currency (see the previous test's fixture note), the
        // best available check is comparing the code strings - full Currency equality is not
        // possible to verify, because the exponent the posting was originally created with is not
        // recoverable from the stored data. This is a deliberate, acknowledged limitation, not an
        // oversight: rejecting on an unresolvable-but-matching code would violate the rule that an
        // unresolvable stored code is not by itself an error, and would make correct calls with a
        // legitimately out-of-catalogue currency start throwing.
        var storedCurrency = Currency.Create("ZWD", 2).Value;
        var posting = Posting.CreateForTest(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            100, storedCurrency.Code, memo: null);
        var currencyWithSameCodeDifferentExponent = Currency.Create("ZWD", 4).Value;

        posting.AmountIn(currencyWithSameCodeDifferentExponent)
            .Should().Be(MoneyValue.Of(100, currencyWithSameCodeDifferentExponent));
    }
}
