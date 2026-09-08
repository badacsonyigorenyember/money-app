using Money.Domain.Primitives;

namespace Money.Domain.Periods;

public abstract record PeriodAnchor
{
    private PeriodAnchor() { }

    public static PeriodAnchor CalendarMonth { get; } = new CalendarMonthAnchor();

    /// <summary>
    /// The month turns over on the first Monday. Unlike the other two anchors the boundary is not
    /// a fixed day number, so it is PeriodResolver.AnchorStart - not AnchorDay - that decides where
    /// a period begins.
    /// </summary>
    public static PeriodAnchor FirstMonday { get; } = new FirstMondayAnchor();

    /// <summary>
    /// Days above 28 are rejected: not every month has them, so a period boundary
    /// would need clamping and would stop tiling cleanly.
    /// </summary>
    public static Result<PeriodAnchor> DayOfMonth(int day) =>
        day is >= 1 and <= 28
            ? Result<PeriodAnchor>.Ok(new DayOfMonthAnchor(day))
            : DomainErrors.Period.AnchorDayOutOfRange(day);

    /// <summary>
    /// The day of the month a period starts on, where that is a fixed number. For
    /// <see cref="FirstMondayAnchor"/> the real boundary moves from month to month, and 1 is only
    /// the placeholder the settings row stores; never compute a boundary from it.
    /// </summary>
    public int AnchorDay => this switch
    {
        CalendarMonthAnchor => 1,
        FirstMondayAnchor => 1,
        DayOfMonthAnchor d => d.Day,
        _ => throw new NotSupportedException($"Unhandled anchor {GetType().Name}.")
    };

    public sealed record CalendarMonthAnchor : PeriodAnchor;

    public sealed record FirstMondayAnchor : PeriodAnchor;

    public sealed record DayOfMonthAnchor(int Day) : PeriodAnchor;
}
