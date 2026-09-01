using Money.Domain.Primitives;

namespace Money.Domain.Periods;

public sealed record PeriodDefinition
{
    private PeriodDefinition(PeriodAnchor anchor, string timeZoneId, DayOfWeek firstDayOfWeek)
    {
        Anchor = anchor;
        TimeZoneId = timeZoneId;
        FirstDayOfWeek = firstDayOfWeek;
    }

    public PeriodAnchor Anchor { get; }

    public string TimeZoneId { get; }

    public DayOfWeek FirstDayOfWeek { get; }

    public static PeriodDefinition Default { get; } =
        new(PeriodAnchor.CalendarMonth, "Europe/Budapest", DayOfWeek.Monday);

    public static Result<PeriodDefinition> Create(
        PeriodAnchor anchor, string timeZoneId, DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        if (!TryFindTimeZone(timeZoneId, out _)) return DomainErrors.Period.UnknownTimeZone(timeZoneId ?? "");

        return Result<PeriodDefinition>.Ok(new PeriodDefinition(anchor, timeZoneId, firstDayOfWeek));
    }

    internal static bool TryFindTimeZone(string? id, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id)) return false;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException
                                      or ArgumentException)
        {
            return false;
        }
    }
}
