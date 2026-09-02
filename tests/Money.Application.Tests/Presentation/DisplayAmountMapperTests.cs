using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Money;

namespace Money.Application.Tests.Presentation;

public sealed class DisplayAmountMapperTests
{
    [Theory]
    [InlineData(AccountKind.Asset, false)]
    [InlineData(AccountKind.Expense, false)]
    [InlineData(AccountKind.Income, true)]
    [InlineData(AccountKind.Liability, true)]
    [InlineData(AccountKind.Equity, true)]
    public void Only_income_liability_and_equity_are_negated_for_display(AccountKind kind, bool negated)
    {
        DisplayAmountMapper.IsNegatedForDisplay(kind).Should().Be(negated);
    }

    [Fact]
    public void A_salary_stored_as_a_credit_is_displayed_as_a_positive_number()
    {
        // Internally: Income:Salary -300000. The user must see 3000.00.
        DisplayAmountMapper.ToDisplay(-300_000, AccountKind.Income, Currency.Eur).Should().Be(3000.00m);
    }

    [Fact]
    public void An_expense_stored_as_a_debit_is_displayed_as_a_positive_number()
    {
        DisplayAmountMapper.ToDisplay(2000, AccountKind.Expense, Currency.Eur).Should().Be(20.00m);
    }

    [Fact]
    public void A_bank_balance_keeps_its_natural_sign()
    {
        DisplayAmountMapper.ToDisplay(100_000, AccountKind.Asset, Currency.Eur).Should().Be(1000.00m);
        DisplayAmountMapper.ToDisplay(-500, AccountKind.Asset, Currency.Eur).Should().Be(-5.00m);
    }

    [Theory]
    [InlineData(AccountKind.Asset)]
    [InlineData(AccountKind.Expense)]
    [InlineData(AccountKind.Income)]
    [InlineData(AccountKind.Liability)]
    [InlineData(AccountKind.Equity)]
    public void Converting_to_display_and_back_returns_the_stored_amount(AccountKind kind)
    {
        foreach (var stored in new long[] { -300_000, -1, 0, 1, 2345, 999_999 })
        {
            var displayed = DisplayAmountMapper.ToDisplay(stored, kind, Currency.Eur);
            DisplayAmountMapper.ToStored(displayed, kind, Currency.Eur).Should().Be(stored);
        }
    }

    [Fact]
    public void A_zero_decimal_currency_is_not_scaled()
    {
        var jpy = Currency.FromCode("JPY").Value;

        DisplayAmountMapper.ToDisplay(2345, AccountKind.Expense, jpy).Should().Be(2345m);
        DisplayAmountMapper.ToStored(2345m, AccountKind.Expense, jpy).Should().Be(2345);
    }
}
