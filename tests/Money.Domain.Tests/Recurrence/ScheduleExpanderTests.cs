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
    public void A_monthly_rule_left_alone_fires_on_weekends_like_any_other_day()
    {
        var schedule = Schedule.MonthlyOnDay(1, 1).Value;

        // 1 August 2026 is a Saturday and 1 November a Sunday. Without the option, both stand.
        Expand(schedule, D(2026, 8, 1), D(2026, 8, 1), D(2026, 12, 31))
            .Should().Equal(D(2026, 8, 1), D(2026, 9, 1), D(2026, 10, 1), D(2026, 11, 1), D(2026, 12, 1));
    }

    [Fact]
    public void A_monthly_rule_that_avoids_weekends_lands_on_the_following_monday()
    {
        var schedule = Schedule.MonthlyOnDay(1, 1, moveOffWeekends: true).Value;

        // Saturday the 1st becomes Monday the 3rd, Sunday the 1st Monday the 2nd. September,
        // October and December start on a weekday and are left where they are.
        Expand(schedule, D(2026, 8, 1), D(2026, 8, 1), D(2026, 12, 31))
            .Should().Equal(D(2026, 8, 3), D(2026, 9, 1), D(2026, 10, 1), D(2026, 11, 2), D(2026, 12, 1));
    }

    [Fact]
    public void A_weekend_move_can_carry_an_occurrence_into_the_next_month()
    {
        var schedule = Schedule.MonthlyOnDay(1, 31, moveOffWeekends: true).Value;

        // The 31st clamps to Saturday 31 January and Saturday 28 February, and each rolls forward
        // over the month boundary - so March holds two occurrences and January none.
        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2026, 3, 31))
            .Should().Equal(D(2026, 2, 2), D(2026, 3, 2), D(2026, 3, 31));
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
    public void A_yearly_rule_can_avoid_weekends_too()
    {
        var schedule = Schedule.YearlyOnDay(1, 8, 1, moveOffWeekends: true).Value;

        // 1 August falls on a Saturday in 2026, a Sunday in 2027 and a Tuesday in 2028.
        Expand(schedule, D(2026, 1, 1), D(2026, 1, 1), D(2028, 12, 31))
            .Should().Equal(D(2026, 8, 3), D(2027, 8, 2), D(2028, 8, 1));
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
}
