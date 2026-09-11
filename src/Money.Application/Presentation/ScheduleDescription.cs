using System.Globalization;
using Money.Domain.Recurrence;

namespace Money.Application.Presentation;

/// <summary>
/// A schedule in the words a user would use for it - "every month on the 10th", "every month on
/// the 1st, or the next weekday". The rules screen shows this instead of the stored fields, so
/// nobody has to read a frequency enum to know when their rent goes out.
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

            RecurrenceFrequency.Monthly =>
                Every(schedule.Interval, "month") + " " + OnDay(schedule) + OrNextWeekday(schedule),

            RecurrenceFrequency.Yearly =>
                Every(schedule.Interval, "year") + " " + OnDay(schedule) +
                " of " + MonthName(schedule.Month!.Value) + OrNextWeekday(schedule),

            RecurrenceFrequency.Custom => "Every " + string.Join(", ", Parts(schedule)),

            _ => "Repeats"
        };
    }

    private static string OnDay(Schedule schedule) =>
        $"on the {Ordinal(schedule.DayOfMonth!.Value)}";

    private static string OrNextWeekday(Schedule schedule) =>
        schedule.MoveOffWeekends ? ", or the next weekday" : "";

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
