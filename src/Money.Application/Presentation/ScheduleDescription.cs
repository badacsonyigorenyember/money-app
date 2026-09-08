using System.Globalization;
using Money.Domain.Recurrence;

namespace Money.Application.Presentation;

/// <summary>
/// A schedule in the words a user would use for it - "on the 10th of every month", "every first
/// Monday". The rules screen shows this instead of the stored fields, so nobody has to read a
/// frequency enum to know when their rent goes out.
/// </summary>
public static class ScheduleDescription
{
    public static string Describe(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return schedule.Frequency switch
        {
            RecurrenceFrequency.Daily => schedule.Interval == 1
                ? "Every day"
                : $"Every {schedule.Interval} days",

            RecurrenceFrequency.Weekly => schedule.Interval == 1
                ? $"Every {DayName(schedule.DayOfWeek!.Value)}"
                : $"Every {schedule.Interval} weeks on {DayName(schedule.DayOfWeek!.Value)}",

            RecurrenceFrequency.Monthly => Every(schedule.Interval, "month") + " " + InMonth(schedule),

            RecurrenceFrequency.Yearly =>
                Every(schedule.Interval, "year") + " " + InMonth(schedule) +
                " of " + MonthName(schedule.Month!.Value),

            RecurrenceFrequency.Custom => "Every " + string.Join(", ", Parts(schedule)),

            _ => "Repeats"
        };
    }

    private static string InMonth(Schedule schedule) =>
        schedule.WeekOfMonth is { } week
            ? $"on the {Ordinal(week)} {DayName(schedule.DayOfWeek!.Value)}"
            : $"on the {Ordinal(schedule.DayOfMonth!.Value)}";

    private static string Every(int interval, string unit) =>
        interval == 1 ? $"Every {unit}" : $"Every {interval} {unit}s";

    private static IEnumerable<string> Parts(Schedule schedule)
    {
        if (schedule.CustomYears > 0) yield return Plural(schedule.CustomYears, "year");
        if (schedule.CustomMonths > 0) yield return Plural(schedule.CustomMonths, "month");
        if (schedule.CustomDays > 0) yield return Plural(schedule.CustomDays, "day");
    }

    private static string Plural(int count, string unit) =>
        count == 1 ? unit : $"{count} {unit}s";

    private static string Ordinal(int n) => n switch
    {
        -1 => "last",
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        21 => "21st",
        22 => "22nd",
        23 => "23rd",
        31 => "31st",
        _ => $"{n}th"
    };

    private static string DayName(DayOfWeek day) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(day);

    private static string MonthName(int month) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
}
