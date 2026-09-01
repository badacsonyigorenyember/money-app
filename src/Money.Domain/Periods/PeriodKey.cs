using System.Globalization;
using Money.Domain.Primitives;

namespace Money.Domain.Periods;

public readonly record struct PeriodKey
{
    private PeriodKey(PeriodType type, int year, int index)
    {
        Type = type;
        Year = year;
        Index = index;
    }

    public PeriodType Type { get; }

    public int Year { get; }

    public int Index { get; }

    public static Result<PeriodKey> Create(PeriodType type, int year, int index)
    {
        if (year is < 1 or > 9999) return DomainErrors.Period.IndexOutOfRange(type.ToString(), year);

        var maxIndex = type switch
        {
            PeriodType.Weekly => 53,
            PeriodType.Monthly => 12,
            PeriodType.Quarterly => 4,
            PeriodType.Yearly => 1,
            _ => throw new NotSupportedException($"Unhandled period type {type}.")
        };

        return index >= 1 && index <= maxIndex
            ? Result<PeriodKey>.Ok(new PeriodKey(type, year, index))
            : DomainErrors.Period.IndexOutOfRange(type.ToString(), index);
    }

    public string ToKeyString() => Type switch
    {
        PeriodType.Weekly => $"{Year:D4}-W{Index:D2}",
        PeriodType.Monthly => $"{Year:D4}-M{Index:D2}",
        PeriodType.Quarterly => $"{Year:D4}-Q{Index:D1}",
        PeriodType.Yearly => $"{Year:D4}-Y",
        _ => throw new NotSupportedException($"Unhandled period type {Type}.")
    };

    public static Result<PeriodKey> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DomainErrors.Period.UnparsableKey(text ?? "");

        var trimmed = text.Trim();
        if (trimmed.Length < 6 || trimmed[4] != '-') return DomainErrors.Period.UnparsableKey(trimmed);

        if (!int.TryParse(trimmed.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            return DomainErrors.Period.UnparsableKey(trimmed);

        var type = trimmed[5] switch
        {
            'W' => PeriodType.Weekly,
            'M' => PeriodType.Monthly,
            'Q' => PeriodType.Quarterly,
            'Y' => PeriodType.Yearly,
            _ => (PeriodType?)null
        };

        if (type is null) return DomainErrors.Period.UnparsableKey(trimmed);

        if (type == PeriodType.Yearly)
        {
            return trimmed.Length == 6
                ? Create(PeriodType.Yearly, year, 1)
                : DomainErrors.Period.UnparsableKey(trimmed);
        }

        if (!int.TryParse(trimmed.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return DomainErrors.Period.UnparsableKey(trimmed);

        return Create(type.Value, year, index);
    }

    public override string ToString() => ToKeyString();
}
