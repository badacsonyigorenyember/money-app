using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Money;

public sealed class MoneyTests
{
    private static readonly Currency Eur = Currency.Eur;
    private static readonly Currency Usd = Currency.Usd;

    [Fact]
    public void Addition_and_subtraction_work_in_minor_units()
    {
        (MoneyValue.Of(2000, Eur) + MoneyValue.Of(345, Eur)).AmountMinor.Should().Be(2345);
        (MoneyValue.Of(2000, Eur) - MoneyValue.Of(345, Eur)).AmountMinor.Should().Be(1655);
    }

    [Fact]
    public void Arithmetic_between_different_currencies_throws()
    {
        var act = () => _ = MoneyValue.Of(100, Eur) + MoneyValue.Of(100, Usd);

        act.Should().Throw<CurrencyMismatchException>().WithMessage("*EUR*USD*");
    }

    [Fact]
    public void Subtraction_between_different_currencies_throws()
    {
        var act = () => _ = MoneyValue.Of(100, Eur) - MoneyValue.Of(100, Usd);

        act.Should().Throw<CurrencyMismatchException>().WithMessage("*EUR*USD*");
    }

    [Fact]
    public void Of_rejects_a_null_currency()
    {
        var act = () => MoneyValue.Of(100, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Add_rejects_a_null_operand()
    {
        var act = () => _ = MoneyValue.Of(100, Eur).Add(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Subtract_rejects_a_null_operand()
    {
        var act = () => _ = MoneyValue.Of(100, Eur).Subtract(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Negation_flips_the_sign_and_keeps_the_currency()
    {
        var negated = -MoneyValue.Of(2500, Eur);

        negated.AmountMinor.Should().Be(-2500);
        negated.Currency.Should().Be(Eur);
    }

    [Fact]
    public void Zero_is_zero_in_its_currency()
    {
        MoneyValue.Zero(Eur).IsZero.Should().BeTrue();
        MoneyValue.Zero(Eur).Sign.Should().Be(0);
        MoneyValue.Of(-1, Eur).Sign.Should().Be(-1);
        MoneyValue.Of(1, Eur).Sign.Should().Be(1);
    }

    [Fact]
    public void Conversion_to_decimal_divides_by_the_minor_unit_scale()
    {
        MoneyValue.Of(2345, Eur).ToDecimal().Should().Be(23.45m);
        MoneyValue.Of(2345, Currency.FromCode("JPY").Value).ToDecimal().Should().Be(2345m);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(-0.5, -1)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    [InlineData(-2.5, -3)]
    [InlineData(0.4999, 0)]
    public void Rounding_is_half_away_from_zero(decimal input, long expected)
    {
        MoneyValue.RoundToMinor(input).Should().Be(expected);
    }

    [Fact]
    public void Applying_a_rate_rounds_only_at_the_end()
    {
        MoneyValue.Of(10_000, Eur).ApplyRate(0.0375m).AmountMinor.Should().Be(375);
        MoneyValue.Of(333, Eur).ApplyRate(0.33333m).AmountMinor.Should().Be(111);
    }

    [Fact]
    public void Rounding_that_overflows_a_long_is_detected_rather_than_wrapping()
    {
        var act = () => MoneyValue.RoundToMinor((decimal)long.MaxValue + 100m);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Overflow_is_detected_rather_than_wrapping_silently()
    {
        var act = () => _ = MoneyValue.Of(long.MaxValue, Eur) + MoneyValue.Of(1, Eur);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Negating_the_minimum_long_value_overflows_rather_than_wrapping()
    {
        var act = () => _ = -MoneyValue.Of(long.MinValue, Eur);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Negate_of_the_minimum_long_value_overflows_rather_than_wrapping()
    {
        var act = () => _ = MoneyValue.Of(long.MinValue, Eur).Negate();

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Subtracting_past_the_minimum_long_value_overflows_rather_than_wrapping()
    {
        var act = () => _ = MoneyValue.Of(long.MinValue, Eur) - MoneyValue.Of(1, Eur);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Money_compares_by_value()
    {
        MoneyValue.Of(100, Eur).Should().Be(MoneyValue.Of(100, Eur));
        MoneyValue.Of(100, Eur).Should().NotBe(MoneyValue.Of(100, Usd));
    }

    [Fact]
    public void The_string_form_shows_the_major_amount_and_the_code()
    {
        MoneyValue.Of(-2345, Eur).ToString().Should().Be("-23.45 EUR");
        MoneyValue.Of(2345, Currency.FromCode("JPY").Value).ToString().Should().Be("2345 JPY");
    }
}
