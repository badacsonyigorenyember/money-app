using Money.Application.Contracts;
using Money.Domain.Primitives;
using Money.Domain.Recurrence;

namespace Money.Application.Recurring;

/// <summary>
/// Turns the flat set of fields a form can post into a validated <see cref="Schedule"/>. Every
/// frequency reads only the fields it needs, so a leftover "day of month" left behind by the
/// user switching from Monthly to Weekly is simply ignored rather than rejected.
/// </summary>
internal static class ScheduleFactory
{
    public static Result<Schedule> From(CreateRecurringRuleRequest request)
    {
        if (!Enum.TryParse<RecurrenceFrequency>(request.Frequency, ignoreCase: true, out var frequency))
            return DomainErrors.Schedule.UnknownFrequency(request.Frequency ?? "");

        var interval = request.Interval;
        var dayOfWeek = DayOfWeekOrMonday(request.DayOfWeek);
        var month = request.Month ?? request.StartDate.Month;
        var dayOfMonth = request.DayOfMonth ?? request.StartDate.Day;

        return frequency switch
        {
            RecurrenceFrequency.Daily => Schedule.Daily(interval),

            RecurrenceFrequency.Weekly => Schedule.Weekly(interval, dayOfWeek),

            RecurrenceFrequency.Monthly =>
                Schedule.MonthlyOnDay(interval, dayOfMonth, request.MoveOffWeekends),

            RecurrenceFrequency.Yearly =>
                Schedule.YearlyOnDay(interval, month, dayOfMonth, request.MoveOffWeekends),

            RecurrenceFrequency.Custom =>
                Schedule.Custom(request.CustomYears, request.CustomMonths, request.CustomDays),

            _ => DomainErrors.Schedule.UnknownFrequency(request.Frequency ?? "")
        };
    }

    private static DayOfWeek DayOfWeekOrMonday(string? name) =>
        Enum.TryParse<DayOfWeek>(name, ignoreCase: true, out var day) ? day : DayOfWeek.Monday;
}
