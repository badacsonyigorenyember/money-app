using CsCheck;
using Money.Domain.Recurrence;

namespace Money.Domain.Tests.Recurrence;

/// <summary>
/// Invariant I8: schedule expansion is idempotent for a given (rule, window). This is the
/// property the whole recurring feature rests on - if expansion were not deterministic, the
/// materialiser would post rent twice.
/// </summary>
public sealed class ScheduleIdempotencyPropertyTests
{
    private static readonly Gen<DayOfWeek> AnyDayOfWeek =
        Gen.OneOfConst(DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                       DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday);

    private static readonly Gen<Schedule> AnySchedule =
        Gen.Select(Gen.Int[0, 6], Gen.Int[1, 6], Gen.Int[1, 31], Gen.Int[1, 12],
                   Gen.Int[1, 4], AnyDayOfWeek, Gen.Int[0, 3])
           .Select(t => t.Item1 switch
           {
               0 => Schedule.Daily(t.Item2).Value,
               1 => Schedule.Weekly(t.Item2, t.Item6).Value,
               2 => Schedule.MonthlyOnDay(t.Item2, t.Item3).Value,
               3 => Schedule.MonthlyOnWeekday(t.Item2, t.Item5, t.Item6).Value,
               4 => Schedule.YearlyOnDay(t.Item2, t.Item4, t.Item3).Value,
               5 => Schedule.YearlyOnWeekday(t.Item2, t.Item4, t.Item5, t.Item6).Value,
               _ => Schedule.Custom(t.Item7, t.Item2, t.Item7 + t.Item2).Value
           });

    private static readonly Gen<DateOnly> AnyDate =
        Gen.Int[new DateOnly(2020, 1, 1).DayNumber, new DateOnly(2035, 12, 31).DayNumber]
           .Select(DateOnly.FromDayNumber);

    [Fact]
    public void Expanding_the_same_rule_over_the_same_window_twice_gives_the_same_dates() =>
        Gen.Select(AnySchedule, AnyDate, AnyDate, AnyDate).Sample((schedule, start, a, b) =>
        {
            var (from, to) = a <= b ? (a, b) : (b, a);

            var first = ScheduleExpander.Expand(schedule, start, from, to).ToArray();
            var second = ScheduleExpander.Expand(schedule, start, from, to).ToArray();

            return first.SequenceEqual(second);
        });

    [Fact]
    public void Expanding_a_window_in_two_halves_gives_the_same_dates_as_expanding_it_whole() =>
        Gen.Select(AnySchedule, AnyDate, AnyDate, AnyDate).Sample((schedule, start, a, b) =>
        {
            var (from, to) = a <= b ? (a, b) : (b, a);
            if (from == to) return true;

            var split = DateOnly.FromDayNumber(from.DayNumber + ((to.DayNumber - from.DayNumber) / 2));

            var whole = ScheduleExpander.Expand(schedule, start, from, to).ToArray();
            var halves = ScheduleExpander.Expand(schedule, start, from, split)
                .Concat(ScheduleExpander.Expand(schedule, start, split.AddDays(1), to))
                .ToArray();

            return whole.SequenceEqual(halves);
        });

    [Fact]
    public void Occurrences_come_out_in_order_and_never_repeat() =>
        Gen.Select(AnySchedule, AnyDate, AnyDate).Sample((schedule, start, end) =>
        {
            var dates = ScheduleExpander.Expand(schedule, start, start, end).ToArray();

            return dates.Zip(dates.Skip(1)).All(pair => pair.First < pair.Second);
        });

    [Fact]
    public void No_occurrence_ever_falls_outside_the_window_or_before_the_start() =>
        Gen.Select(AnySchedule, AnyDate, AnyDate, AnyDate).Sample((schedule, start, a, b) =>
        {
            var (from, to) = a <= b ? (a, b) : (b, a);

            return ScheduleExpander.Expand(schedule, start, from, to)
                .All(date => date >= from && date <= to && date >= start);
        });
}
