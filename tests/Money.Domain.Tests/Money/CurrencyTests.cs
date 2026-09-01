using Money.Domain.Money;

namespace Money.Domain.Tests.Money;

public sealed class CurrencyTests
{
    [Fact]
    public void A_known_code_resolves_with_its_minor_unit_exponent()
    {
        Currency.FromCode("EUR").Value.MinorUnitExponent.Should().Be(2);
        Currency.FromCode("JPY").Value.MinorUnitExponent.Should().Be(0);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData(" EUR ")]
    public void Lookup_is_case_and_whitespace_insensitive(string input)
    {
        Currency.FromCode(input).Value.Code.Should().Be("EUR");
    }

    [Fact]
    public void An_unknown_code_is_rejected_rather_than_assumed_to_have_two_decimals()
    {
        var result = Currency.FromCode("XYZ");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("currency.unknown");
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("")]
    public void A_malformed_code_is_rejected(string input)
    {
        Currency.FromCode(input).Error!.Code.Should().Be("currency.invalid_code");
    }

    [Fact]
    public void The_minor_unit_scale_is_ten_to_the_exponent()
    {
        Currency.FromCode("EUR").Value.MinorUnitScale.Should().Be(100m);
        Currency.FromCode("JPY").Value.MinorUnitScale.Should().Be(1m);
    }

    [Fact]
    public void Currencies_compare_by_value()
    {
        Currency.FromCode("EUR").Value.Should().Be(Currency.Eur);
    }

    [Fact]
    public void An_exponent_outside_the_supported_range_is_rejected()
    {
        Currency.Create("EUR", 9).Error!.Code.Should().Be("currency.invalid_minor_unit_exponent");
    }

    [Fact]
    public void A_negative_exponent_is_rejected()
    {
        Currency.Create("XYZ", -1).Error!.Code.Should().Be("currency.invalid_minor_unit_exponent");
    }

    [Fact]
    public void A_null_code_passed_to_FromCode_is_rejected_as_invalid_rather_than_throwing()
    {
        var act = () => Currency.FromCode(null!);

        act.Should().NotThrow();
        Currency.FromCode(null!).Error!.Code.Should().Be("currency.invalid_code");
    }

    [Fact]
    public void Create_returns_the_canonical_instance_for_a_known_code_with_the_matching_exponent()
    {
        Currency.Create("EUR", 2).Value.Should().Be(Currency.Eur);
    }

    [Fact]
    public void Create_rejects_a_known_code_whose_exponent_disagrees_with_the_canonical_one()
    {
        var result = Currency.Create("EUR", 3);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("currency.invalid_minor_unit_exponent");
    }
}
