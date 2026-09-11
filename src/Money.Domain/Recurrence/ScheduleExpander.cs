namespace Money.Domain.Recurrence;

/// <summary>
/// Turns a <see cref="Schedule"/> into the dates it fires on. A pure function of
/// (schedule, start, window) with no clock and no state, which is what makes I8 hold: expanding
/// the same rule over the same window twice yields the identical set, so materialising twice
/// cannot double-post.
/// </summary>
public static class ScheduleExpander
{
    /// <summary>
    /// Occurrence dates in <paramref name="from"/>..<paramref name="to"/> inclusive. Occurrences
    /// before <paramref name="start"/> are never produced: the start date anchors the whole
    /// series, so a monthly rule starting on the 20th does not fire on the 5th of that month.
    /// </summary>
    public static IEnumerable<DateOnly> Expand(
        Schedule schedule, DateOnly start, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (to < start || to < from) yield break;

        for (var step = 0; ; step++)
        {
            if (At(schedule, start, step) is not { } date) yield break;
            if (date > to) yield break;
            if (date >= start && date >= from) yield return date;
        }
    }

    /// <summary>
    /// The <paramref name="step"/>'th candidate date, or null once the series runs off the end of
    /// the calendar. Candidates are non-decreasing in <paramref name="step"/> for every shape
    /// here - including day-31 monthly, which clamps to 28/29/30 - so Expand can stop at the
    /// first one past the window instead of scanning forever.
    /// </summary>
    private static DateOnly? At(Schedule schedule, DateOnly start, int step)
    {
        var n = (long)step;

        return schedule.Frequency switch
        {
            RecurrenceFrequency.Daily =>
                Shift(start, 0, n * schedule.Interval),

            RecurrenceFrequency.Weekly =>
                Shift(OnOrAfter(start, schedule.DayOfWeek!.Value), 0, n * 7 * schedule.Interval),

            RecurrenceFrequency.Monthly =>
                DayIn(schedule, Shift(FirstOf(start), n * schedule.Interval, 0)),

            RecurrenceFrequency.Yearly =>
                DayIn(schedule, Shift(FirstOf(start, schedule.Month!.Value), n * 12 * schedule.Interval, 0)),

            RecurrenceFrequency.Custom =>
                Shift(start, n * ((schedule.CustomYears * 12) + schedule.CustomMonths),
                      n * schedule.CustomDays),

            _ => null
        };
    }

    /// <summary>The day within the month <paramref name="monthStart"/> begins.</summary>
    private static DateOnly? DayIn(Schedule schedule, DateOnly? monthStart)
    {
        if (monthStart is not { } anchor) return null;

        // "The 31st" in a 30-day month means the 30th, and in February the 28th or 29th.
        var length = DateTime.DaysInMonth(anchor.Year, anchor.Month);
        var date = new DateOnly(anchor.Year, anchor.Month, Math.Min(schedule.DayOfMonth!.Value, length));

        return schedule.MoveOffWeekends ? NextWeekday(date) : date;
    }

    /// <summary>
    /// Saturday and Sunday roll forward to the Monday; every other day stands. Forward only, for
    /// two reasons: a backward roll could land before the rule's start date, where Expand would
    /// drop the occurrence without saying so, and it could cross the previous occurrence. Rolling
    /// forward moves a date by at most two days while consecutive monthly candidates are at least
    /// 28 days apart, so the series stays strictly increasing and Expand can still stop at the
    /// first date past the window. The roll may carry a date into the next month - 31 January on a
    /// Saturday becomes 2 February - which is allowed: the occurrence still happens once.
    /// </summary>
    private static DateOnly NextWeekday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(2),
        DayOfWeek.Sunday => date.AddDays(1),
        _ => date
    };

    private static DateOnly OnOrAfter(DateOnly date, DayOfWeek day) =>
        date.AddDays((7 + (int)day - (int)date.DayOfWeek) % 7);

    private static DateOnly FirstOf(DateOnly date) => new(date.Year, date.Month, 1);

    private static DateOnly FirstOf(DateOnly date, int month) => new(date.Year, month, 1);

    /// <summary>
    /// Adds months then days, answering null rather than throwing once the result leaves the
    /// range DateOnly can hold - which is how an unbounded rule terminates its own expansion.
    /// </summary>
    private static DateOnly? Shift(DateOnly date, long months, long days)
    {
        if (months is > 120_000 or < -120_000 || days is > 4_000_000 or < -4_000_000) return null;

        try
        {
            return date.AddMonths((int)months).AddDays((int)days);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
