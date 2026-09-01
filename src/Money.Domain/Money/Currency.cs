using Money.Domain.Primitives;

namespace Money.Domain.Money;

public sealed record Currency
{
    private const int MaxExponent = 4;

    private Currency(string code, int minorUnitExponent)
    {
        Code = code;
        MinorUnitExponent = minorUnitExponent;
    }

    public string Code { get; }

    public int MinorUnitExponent { get; }

    public decimal MinorUnitScale => Pow10(MinorUnitExponent);

    public static Currency Eur { get; } = new("EUR", 2);
    public static Currency Huf { get; } = new("HUF", 2);
    public static Currency Usd { get; } = new("USD", 2);

    private static readonly Dictionary<string, Currency> KnownByCode = new(StringComparer.Ordinal)
    {
        ["EUR"] = Eur,
        ["HUF"] = Huf,
        ["USD"] = Usd,
        ["GBP"] = new("GBP", 2),
        ["CHF"] = new("CHF", 2),
        ["CZK"] = new("CZK", 2),
        ["PLN"] = new("PLN", 2),
        ["RON"] = new("RON", 2),
        ["SEK"] = new("SEK", 2),
        ["DKK"] = new("DKK", 2),
        ["NOK"] = new("NOK", 2),
        ["JPY"] = new("JPY", 0),
    };

    public static IReadOnlyCollection<Currency> Known => KnownByCode.Values;

    public static Result<Currency> FromCode(string code)
    {
        var normalised = Normalise(code);
        if (normalised is null) return DomainErrors.Currency.InvalidCode(code ?? "");

        return KnownByCode.TryGetValue(normalised, out var currency)
            ? Result<Currency>.Ok(currency)
            : DomainErrors.Currency.Unknown(normalised);
    }

    /// <summary>Creates a currency outside the known table. For tests and future import paths.</summary>
    public static Result<Currency> Create(string code, int minorUnitExponent)
    {
        var normalised = Normalise(code);
        if (normalised is null) return DomainErrors.Currency.InvalidCode(code ?? "");
        if (minorUnitExponent is < 0 or > MaxExponent)
            return DomainErrors.Currency.InvalidMinorUnitExponent(minorUnitExponent);

        return Result<Currency>.Ok(new Currency(normalised, minorUnitExponent));
    }

    private static string? Normalise(string? code)
    {
        var trimmed = code?.Trim();
        if (trimmed is not { Length: 3 }) return null;
        if (!trimmed.All(char.IsAsciiLetter)) return null;
        return trimmed.ToUpperInvariant();
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10m;
        return result;
    }

    public override string ToString() => Code;
}
