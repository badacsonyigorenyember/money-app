using Money.Application.Presentation;

namespace Money.Application.Tests.Presentation;

public sealed class CurrencyFormatTests
{
    [Fact]
    public void A_two_decimal_currency_formats_with_two_decimals()
    {
        CurrencyFormat.Format(19.99m, "EUR").Should().Be("19.99");
    }

    [Fact]
    public void A_zero_decimal_currency_formats_with_no_invented_decimals()
    {
        // I3: _TransactionRows.cshtml and _AccountRows.cshtml hardcoded "N2", so a JPY amount
        // (MinorUnitExponent 0) rendered with two decimals that were never actually stored.
        CurrencyFormat.Format(1235m, "JPY").Should().Be("1,235");
    }

    [Fact]
    public void An_unrecognised_currency_code_falls_back_to_two_decimals()
    {
        CurrencyFormat.Format(5m, "XXX").Should().Be("5.00");
    }
}
