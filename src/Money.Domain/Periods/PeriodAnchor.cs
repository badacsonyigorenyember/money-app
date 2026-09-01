using Money.Domain.Primitives;

namespace Money.Domain.Periods;

public abstract record PeriodAnchor
{
    private PeriodAnchor() { }

    public static PeriodAnchor CalendarMonth { get; } = new CalendarMonthAnchor();

    /// <summary>
    /// Days above 28 are rejected: not every month has them, so a period boundary
    /// would need clamping and would stop tiling cleanly.
    /// </summary>
    public static Result<PeriodAnchor> DayOfMonth(int day) =>
        day is >= 1 and <= 28
            ? Result<PeriodAnchor>.Ok(new DayOfMonthAnchor(day))
            : DomainErrors.Period.AnchorDayOutOfRange(day);

    public int AnchorDay => this switch
    {
        CalendarMonthAnchor => 1,
        DayOfMonthAnchor d => d.Day,
        _ => throw new NotSupportedException($"Unhandled anchor {GetType().Name}.")
    };

    public sealed record CalendarMonthAnchor : PeriodAnchor;

    public sealed record DayOfMonthAnchor(int Day) : PeriodAnchor;
}
