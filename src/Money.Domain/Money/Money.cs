using System.Globalization;

namespace Money.Domain.Money;

public sealed record Money
{
    private Money(long amountMinor, Currency currency)
    {
        AmountMinor = amountMinor;
        Currency = currency;
    }

    public long AmountMinor { get; }

    public Currency Currency { get; }

    public bool IsZero => AmountMinor == 0;

    public int Sign => Math.Sign(AmountMinor);

    public static Money Of(long amountMinor, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(amountMinor, currency);
    }

    public static Money Zero(Currency currency) => Of(0, currency);

    public Money Add(Money other)
    {
        RequireSameCurrency(other);
        return new Money(checked(AmountMinor + other.AmountMinor), Currency);
    }

    public Money Subtract(Money other)
    {
        RequireSameCurrency(other);
        return new Money(checked(AmountMinor - other.AmountMinor), Currency);
    }

    public Money Negate() => new(checked(-AmountMinor), Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    /// <summary>Multiplies by an exact decimal factor and rounds once, at the end.</summary>
    public Money ApplyRate(decimal factor) => new(RoundToMinor(AmountMinor * factor), Currency);

    /// <summary>The solution's only rounding function: half away from zero, to a whole minor unit.</summary>
    public static long RoundToMinor(decimal exactMinorUnits) =>
        checked((long)Math.Round(exactMinorUnits, 0, MidpointRounding.AwayFromZero));

    /// <summary>For display and rate arithmetic only. Never round-trip money through this.</summary>
    public decimal ToDecimal() => AmountMinor / Currency.MinorUnitScale;

    private void RequireSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!Currency.Equals(other.Currency)) throw new CurrencyMismatchException(Currency, other.Currency);
    }

    public override string ToString() =>
        ToDecimal().ToString("F" + Currency.MinorUnitExponent.ToString(CultureInfo.InvariantCulture),
                             CultureInfo.InvariantCulture)
        + " " + Currency.Code;
}
