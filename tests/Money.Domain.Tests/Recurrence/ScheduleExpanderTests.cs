using Money.Domain.Recurrence;

namespace Money.Domain.Tests.Recurrence;

public sealed class ScheduleExpanderTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static DateOnly[] Expand(Schedule schedule, DateOnly start, DateOnly from, DateOnly to) =>
        ScheduleExpander.Expand(schedule, start, from, to).ToArray();

    [Fact]
    public void A_daily_rule_fires_every_day()
    {
        var schedule = Schedule.Daily(1).Value;

        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2026, 1, 4))
            .Should().Equal(D(2026, 1, 1), D(2026, 1, 2), D(2026, 1, 3), D(2026, 1, 4));
    }

    [Fact]
    public void An_every_third_day_rule_skips_two_days_between_occurrences()
    {
        var schedule = Schedule.Daily(3).Value;

        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2026, 1, 10))
            .Should().Equal(D(2026, 1, 1), D(2026, 1, 4), D(2026, 1, 7), D(2026, 1, 10));
    }

    [Fact]
    public void A_weekly_rule_starts_on_the_first_matching_weekday_on_or_after_the_start_date()
    {
        // 1 January 2026 is a Thursday, so the first Monday of the series is the 5th.
        var schedule = Schedule.Weekly(1, DayOfWeek.Monday).Value;

        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2026, 1, 20))
            .Should().Equal(D(2026, 1, 5), D(2026, 1, 12), D(2026, 1, 19));
    }

    [Fact]
    public void A_fortnightly_rule_fires_every_other_week()
    {
        var schedule = Schedule.Weekly(2, DayOfWeek.Friday).Value;

        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2026, 2, 15))
            .Should().Equal(D(2026, 1, 2), D(2026, 1, 16), D(2026, 1, 30), D(2026, 2, 13));
    }

    [Fact]
    public void A_monthly_rule_on_the_tenth_fires_on_the_tenth_of_every_month()
    {
        var schedule = Schedule.MonthlyOnDay(1, 10).Value;

        Expand(schedule, D(2026, 1, 10), D(2026, 1, 1), D(2026, 4, 30))
            .Should().Equal(D(2026, 1, 10), D(2026, 2, 10), D(2026, 3, 10), D(2026, 4, 10));
    }

    [Fact]
    public void A_monthly_rule_on_the_31st_clamps_to_the_last_day_of_shorter_months()
    {
        var schedule = Schedule.MonthlyOnDay(1, 31).Value;

        Expand(schedule, D(2026, 1, 31), D(2026, 1, 1), D(2026, 6, 30))
            .Should().Equal(
                D(2026, 1, 31), D(2026, 2, 28), D(2026, 3, 31),
                D(2026, 4, 30), D(2026, 5, 31), D(2026, 6, 30));
    }

    [Fact]
    public void A_monthly_rule_on_the_31st_lands_on_29_February_in_a_leap_year()
    {
        var schedule = Schedule.MonthlyOnDay(1, 31).Value;

        Expand(schedule, D(2028, 1, 31), D(2028, 2, 1), D(2028, 2, 29))
            .Should().Equal(D(2028, 2, 29));
    }

    [Fact]
    public void A_monthly_rule_on_the_first_monday_moves_with_the_calendar()
    {
        var schedule = Schedule.MonthlyOnWeekday(1, 1, DayOfWeek.Monday).Value;

        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2026, 4, 30))
            .Should().Equal(D(2026, 1, 5), D(2026, 2, 2), D(2026, 3, 2), D(2026, 4, 6));
    }

    [Fact]
    public void A_monthly_rule_on_the_last_friday_takes_the_fifth_friday_when_there_is_one()
    {
        var schedule = Schedule.MonthlyOnWeekday(1, -1, DayOfWeek.Friday).Value;

        // January 2026 has five Fridays (2, 9, 16, 23, 30); February has four (6, 13, 20, 27).
        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2026, 2, 28))
            .Should().Equal(D(2026, 1, 30), D(2026, 2, 27));
    }

    [Fact]
    public void A_yearly_rule_fires_on_the_same_date_each_year()
    {
        var schedule = Schedule.YearlyOnDay(1, 3, 15).Value;

        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2028, 12, 31))
            .Should().Equal(D(2026, 3, 15), D(2027, 3, 15), D(2028, 3, 15));
    }

    [Fact]
    public void A_yearly_rule_on_29_February_clamps_in_non_leap_years()
    {
        var schedule = Schedule.YearlyOnDay(1, 2, 29).Value;

        Expand(schedule, D(2028, 1, 1), D(2028, 1, 1), D(2030, 12, 31))
            .Should().Equal(D(2028, 2, 29), D(2029, 2, 28), D(2030, 2, 28));
    }

    [Fact]
    public void A_yearly_rule_can_target_the_first_monday_of_a_month()
    {
        var schedule = Schedule.YearlyOnWeekday(1, 9, 1, DayOfWeek.Monday).Value;

        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2028, 12, 31))
            .Should().Equal(D(2026, 9, 7), D(2027, 9, 6), D(2028, 9, 4));
    }

    [Fact]
    public void A_custom_rule_advances_by_years_months_and_days_together()
    {
        var schedule = Schedule.Custom(1, 2, 3).Value;

        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2029, 12, 31))
            .Should().Equal(D(2026, 1, 1), D(2027, 3, 4), D(2028, 5, 7), D(2029, 7, 10));
    }

    [Fact]
    public void Occurrences_before_the_start_date_are_never_produced()
    {
        var schedule = Schedule.MonthlyOnDay(1, 5).Value;

        Expand(schedule, D(2026, 3, 20), D(2026, 1, 1), D(2026, 5, 31))
            .Should().Equal(D(2026, 4, 5), D(2026, 5, 5));
    }

    [Fact]
    public void A_window_that_ends_before_the_start_date_yields_nothing()
    {
        var schedule = Schedule.Daily(1).Value;

        Expand(schedule, D(2026, 3, 1), D(2026, 1, 1), D(2026, 2, 28)).Should().BeEmpty();
    }

    [Fact]
    public void A_rule_running_off_the_end_of_the_calendar_terminates()
    {
        var schedule = Schedule.YearlyOnDay(1, 1, 1).Value;

        Expand(schedule, D(9990, 1, 1), D(9990, 1, 1), DateOnly.MaxValue)
            .Should().HaveCount(10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void An_interval_outside_one_to_a_thousand_is_rejected(int interval) =>
        Schedule.Daily(interval).IsFailure.Should().BeTrue();

    [Fact]
    public void A_custom_step_of_nothing_is_rejected() =>
        Schedule.Custom(0, 0, 0).IsFailure.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void A_day_of_month_outside_one_to_thirty_one_is_rejected(int day) =>
        Schedule.MonthlyOnDay(1, day).IsFailure.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-2)]
    public void A_week_of_month_other_than_one_to_four_or_last_is_rejected(int week) =>
        Schedule.MonthlyOnWeekday(1, week, DayOfWeek.Monday).IsFailure.Should().BeTrue();
}
