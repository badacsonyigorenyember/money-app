using Money.Domain.Time;

namespace Money.Domain.Periods;

/// <summary>
/// The single component that computes period boundaries. Payday-anchored budgeting stays a
/// setting rather than a rewrite only because nothing else does month arithmetic.
/// </summary>
public sealed class PeriodResolver
{
    private readonly PeriodDefinition _definition;
    private readonly int _anchorDay;
    private readonly TimeZoneInfo _timeZone;

    public PeriodResolver(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
        _anchorDay = definition.Anchor.AnchorDay;

        if (!PeriodDefinition.TryFindTimeZone(definition.TimeZoneId, out var timeZone))
        {
            throw new ArgumentException(
                $"Time zone '{definition.TimeZoneId}' is unknown. PeriodDefinition.Create validates this; " +
                "a resolver must never be built from an unvalidated definition.", nameof(definition));
        }

        _timeZone = timeZone;
    }

    public PeriodDefinition Definition => _definition;

    /// <summary>Today's date in the configured time zone. The only UTC-to-local conversion in the domain.</summary>
    public DateOnly TodayIn(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, _timeZone).DateTime);
    }

    public PeriodKey Resolve(DateOnly date, PeriodType type) => type switch
    {
        PeriodType.Weekly => ResolveWeekly(date),
        PeriodType.Monthly => ResolveMonthly(date),
        PeriodType.Quarterly => ResolveQuarterly(date),
        PeriodType.Yearly => ResolveYearly(date),
        _ => throw new NotSupportedException($"Unhandled period type {type}.")
    };

    public DateRange Range(PeriodKey key) => key.Type switch
    {
        PeriodType.Weekly => WeeklyRange(key),
        PeriodType.Monthly => MonthlyRange(key),
        PeriodType.Quarterly => QuarterlyRange(key),
        PeriodType.Yearly => YearlyRange(key),
        _ => throw new NotSupportedException($"Unhandled period type {key.Type}.")
    };

    public PeriodKey Next(PeriodKey key) => Resolve(Range(key).EndExclusive, key.Type);

    public PeriodKey Previous(PeriodKey key) => Resolve(Range(key).Start.AddDays(-1), key.Type);

    // ---- monthly, quarterly, yearly all sit on the anchored month ------------------

    /// <summary>The (year, month) whose anchored period contains <paramref name="date"/>.</summary>
    private (int Year, int Month) AnchoredMonth(DateOnly date) =>
        date.Day >= _anchorDay
            ? (date.Year, date.Month)
            : date.Month == 1 ? (date.Year - 1, 12) : (date.Year, date.Month - 1);

    private PeriodKey ResolveMonthly(DateOnly date)
    {
        var (year, month) = AnchoredMonth(date);
        return Key(PeriodType.Monthly, year, month);
    }

    private DateRange MonthlyRange(PeriodKey key)
    {
        var start = new DateOnly(key.Year, key.Index, _anchorDay);
        var (nextYear, nextMonth) = key.Index == 12 ? (key.Year + 1, 1) : (key.Year, key.Index + 1);
        return DateRange.FromOrdered(start, new DateOnly(nextYear, nextMonth, _anchorDay));
    }

    private PeriodKey ResolveQuarterly(DateOnly date)
    {
        var (year, month) = AnchoredMonth(date);
        return Key(PeriodType.Quarterly, year, ((month - 1) / 3) + 1);
    }

    private DateRange QuarterlyRange(PeriodKey key)
    {
        var startMonth = ((key.Index - 1) * 3) + 1;
        var start = new DateOnly(key.Year, startMonth, _anchorDay);
        var endMonth = startMonth + 3;
        var end = endMonth > 12
            ? new DateOnly(key.Year + 1, endMonth - 12, _anchorDay)
            : new DateOnly(key.Year, endMonth, _anchorDay);
        return DateRange.FromOrdered(start, end);
    }

    private PeriodKey ResolveYearly(DateOnly date) => Key(PeriodType.Yearly, AnchoredMonth(date).Year, 1);

    private DateRange YearlyRange(PeriodKey key) =>
        DateRange.FromOrdered(new DateOnly(key.Year, 1, _anchorDay),
                              new DateOnly(key.Year + 1, 1, _anchorDay));

    // ---- weekly -------------------------------------------------------------------

    private DateOnly WeekStart(DateOnly date)
    {
        var offset = (7 + (int)date.DayOfWeek - (int)_definition.FirstDayOfWeek) % 7;
        return date.AddDays(-offset);
    }

    private PeriodKey ResolveWeekly(DateOnly date)
    {
        var midweek = WeekStart(date).AddDays(3);
        return Key(PeriodType.Weekly, midweek.Year, ((midweek.DayOfYear - 1) / 7) + 1);
    }

    private DateRange WeeklyRange(PeriodKey key)
    {
        // Under the midweek rule the week containing 4 January is always week 1.
        var start = WeekStart(new DateOnly(key.Year, 1, 4)).AddDays(7 * (key.Index - 1));

        if (ResolveWeekly(start) != key)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key), key, $"Week {key.Index} does not exist in {key.Year}.");
        }

        return DateRange.FromOrdered(start, start.AddDays(7));
    }

    private static PeriodKey Key(PeriodType type, int year, int index) =>
        PeriodKey.Create(type, year, index).Value;
}
