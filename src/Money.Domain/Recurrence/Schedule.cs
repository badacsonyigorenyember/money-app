using Money.Domain.Primitives;

namespace Money.Domain.Recurrence;

/// <summary>
/// When a recurring rule fires, relative to its own start date (spec 5.8). Deliberately flat
/// rather than a hierarchy: every field maps to one column and to one form control, and the
/// factories below are the only way to reach a combination that <see cref="ScheduleExpander"/>
/// can read. An instance is therefore always internally consistent.
/// </summary>
public sealed record Schedule
{
    // EF materialisation constructor.
    private Schedule() { }

    public RecurrenceFrequency Frequency { get; private set; }

    /// <summary>Every <c>Interval</c> days/weeks/months/years. Always at least 1.</summary>
    public int Interval { get; private set; } = 1;

    /// <summary>Weekly only: which day of the week the rule fires on.</summary>
    public DayOfWeek? DayOfWeek { get; private set; }

    /// <summary>
    /// Monthly/Yearly: a date landing on a Saturday or Sunday fires on the following Monday
    /// instead. What a salary paid "on the 1st" actually does when the 1st is a weekend.
    /// </summary>
    public bool MoveOffWeekends { get; private set; }

    /// <summary>1-31, clamped down to the length of each month, so 31 means 28 in February.</summary>
    public int? DayOfMonth { get; private set; }

    /// <summary>1-12. Yearly only.</summary>
    public int? Month { get; private set; }

    public int CustomYears { get; private set; }
    public int CustomMonths { get; private set; }
    public int CustomDays { get; private set; }

    public static Result<Schedule> Daily(int interval)
    {
        if (BadInterval(interval) is { } error) return error;

        return Result<Schedule>.Ok(new Schedule
        {
            Frequency = RecurrenceFrequency.Daily,
            Interval = interval
        });
    }

    public static Result<Schedule> Weekly(int interval, DayOfWeek dayOfWeek)
    {
        if (BadInterval(interval) is { } error) return error;

        return Result<Schedule>.Ok(new Schedule
        {
            Frequency = RecurrenceFrequency.Weekly,
            Interval = interval,
            DayOfWeek = dayOfWeek
        });
    }

    public static Result<Schedule> MonthlyOnDay(
        int interval, int dayOfMonth, bool moveOffWeekends = false)
    {
        if ((BadInterval(interval) ?? BadDay(dayOfMonth)) is { } error) return error;

        return Result<Schedule>.Ok(new Schedule
        {
            Frequency = RecurrenceFrequency.Monthly,
            Interval = interval,
            DayOfMonth = dayOfMonth,
            MoveOffWeekends = moveOffWeekends
        });
    }

    public static Result<Schedule> YearlyOnDay(
        int interval, int month, int dayOfMonth, bool moveOffWeekends = false)
    {
        if ((BadInterval(interval) ?? BadMonth(month) ?? BadDay(dayOfMonth)) is { } error) return error;

        return Result<Schedule>.Ok(new Schedule
        {
            Frequency = RecurrenceFrequency.Yearly,
            Interval = interval,
            Month = month,
            DayOfMonth = dayOfMonth,
            MoveOffWeekends = moveOffWeekends
        });
    }

    /// <summary>
    /// A step of years + months + days, applied together. At least one has to be non-zero, or
    /// expansion would never advance.
    /// </summary>
    public static Result<Schedule> Custom(int years, int months, int days)
    {
        if (years < 0 || months < 0 || days < 0 || (years == 0 && months == 0 && days == 0))
            return DomainErrors.Schedule.CustomStepEmpty();

        if (years > 100 || months > 1200 || days > 36500)
            return DomainErrors.Schedule.CustomStepTooLarge();

        return Result<Schedule>.Ok(new Schedule
        {
            Frequency = RecurrenceFrequency.Custom,
            CustomYears = years,
            CustomMonths = months,
            CustomDays = days
        });
    }

    private static DomainError? BadInterval(int interval) =>
        interval is >= 1 and <= 1000 ? null : DomainErrors.Schedule.IntervalOutOfRange(interval);

    private static DomainError? BadDay(int dayOfMonth) =>
        dayOfMonth is >= 1 and <= 31 ? null : DomainErrors.Schedule.DayOfMonthOutOfRange(dayOfMonth);

    private static DomainError? BadMonth(int month) =>
        month is >= 1 and <= 12 ? null : DomainErrors.Schedule.MonthOutOfRange(month);
}
