using System.Globalization;
using Money.Domain.Money;

namespace Money.Application.Presentation;

/// <summary>
/// Formats an already-oriented display amount for a view. <see cref="Money.Domain.Money.Money"/>.
/// ToString() already gets this right for a stored Money value; this covers the views that only
/// have a decimal amount and a currency code (already converted to display units) rather than a
/// Money instance - so the number of decimal places still has to come from the currency, not a
/// hardcoded "N2" (I3: a JPY amount rendered with two invented decimals it never had).
/// </summary>
public static class CurrencyFormat
{
    public static string Format(decimal displayAmount, string currencyCode)
    {
        var exponent = Currency.FromCode(currencyCode) is { IsSuccess: true } result
            ? result.Value.MinorUnitExponent
            : 2;

        return displayAmount.ToString(
            "N" + exponent.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
