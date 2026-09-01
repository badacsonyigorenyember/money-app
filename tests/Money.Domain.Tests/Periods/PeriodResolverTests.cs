using Money.Domain.Periods;
using Money.TestSupport;

namespace Money.Domain.Tests.Periods;

public sealed class PeriodResolverTests
{
    private static PeriodResolver CalendarMonth() =>
        new(PeriodDefinition.Create(PeriodAnchor.CalendarMonth, "Europe/Budapest", DayOfWeek.Monday).Value);

    private static PeriodResolver AnchoredOn(int day) =>
        new(PeriodDefinition.Create(PeriodAnchor.DayOfMonth(day).Value, "Europe/Budapest", DayOfWeek.Monday).Value);

    [Fact]
    public void A_calendar_month_period_runs_from_the_first_to_the_first()
    {
        var range = CalendarMonth().Range(PeriodKey.Create(PeriodType.Monthly, 2026, 9).Value);

        range.Start.Should().Be(new DateOnly(2026, 9, 1));
        range.EndExclusive.Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void A_period_anchored_on_day_twenty_five_is_labelled_by_the_month_it_starts_in()
    {
        // Spec 5.4: "the period from 25 September to 24 October is 2026-M09"
        var resolver = AnchoredOn(25);

        resolver.Resolve(new DateOnly(2026, 9, 25), PeriodType.Monthly).ToKeyString().Should().Be("2026-M09");
        resolver.Resolve(new DateOnly(2026, 10, 24), PeriodType.Monthly).ToKeyString().Should().Be("2026-M09");
        resolver.Resolve(new DateOnly(2026, 10, 25), PeriodType.Monthly).ToKeyString().Should().Be("2026-M10");
        resolver.Resolve(new DateOnly(2026, 9, 24), PeriodType.Monthly).ToKeyString().Should().Be("2026-M08");
    }

    [Fact]
    public void An_anchored_range_spans_two_calendar_months()
    {
        var range = AnchoredOn(25).Range(PeriodKey.Create(PeriodType.Monthly, 2026, 9).Value);

        range.Start.Should().Be(new DateOnly(2026, 9, 25));
        range.EndExclusive.Should().Be(new DateOnly(2026, 10, 25));
    }

    [Fact]
    public void An_anchored_year_starts_on_the_anchor_day_of_january()
    {
        var resolver = AnchoredOn(25);

        resolver.Resolve(new DateOnly(2026, 1, 10), PeriodType.Yearly).Year.Should().Be(2025);
        resolver.Range(PeriodKey.Create(PeriodType.Yearly, 2026, 1).Value).Start
            .Should().Be(new DateOnly(2026, 1, 25));
    }

    [Fact]
    public void An_anchored_quarter_starts_on_the_anchor_day_of_the_quarter_s_first_month()
    {
        var range = AnchoredOn(25).Range(PeriodKey.Create(PeriodType.Quarterly, 2026, 4).Value);

        range.Start.Should().Be(new DateOnly(2026, 10, 25));
        range.EndExclusive.Should().Be(new DateOnly(2027, 1, 25));
    }

    [Fact]
    public void Weeks_follow_iso_8601_when_the_week_starts_on_monday()
    {
        var resolver = CalendarMonth();

        // 2026-01-01 is a Thursday, so it is in ISO week 1 of 2026,
        // and 2025-12-29 is the Monday that starts that same week.
        resolver.Resolve(new DateOnly(2026, 1, 1), PeriodType.Weekly).ToKeyString().Should().Be("2026-W01");
        resolver.Resolve(new DateOnly(2025, 12, 29), PeriodType.Weekly).ToKeyString().Should().Be("2026-W01");
        resolver.Range(PeriodKey.Create(PeriodType.Weekly, 2026, 1).Value).Start
            .Should().Be(new DateOnly(2025, 12, 29));
    }

    [Fact]
    public void Next_and_previous_walk_the_timeline_without_gaps()
    {
        var resolver = AnchoredOn(25);
        var september = PeriodKey.Create(PeriodType.Monthly, 2026, 9).Value;

        var october = resolver.Next(september);

        october.ToKeyString().Should().Be("2026-M10");
        resolver.Range(october).Start.Should().Be(resolver.Range(september).EndExclusive);
        resolver.Previous(october).Should().Be(september);
    }

    [Fact]
    public void Next_rolls_over_the_year_boundary()
    {
        var resolver = CalendarMonth();

        resolver.Next(PeriodKey.Create(PeriodType.Monthly, 2026, 12).Value)
            .ToKeyString().Should().Be("2027-M01");
        resolver.Previous(PeriodKey.Create(PeriodType.Monthly, 2026, 1).Value)
            .ToKeyString().Should().Be("2025-M12");
    }

    [Fact]
    public void Today_is_the_date_in_the_configured_time_zone_not_in_utc()
    {
        // 23:30 UTC on 31 August is already 01:30 on 1 September in Budapest (UTC+2 in summer).
        CalendarMonth().TodayIn(FakeClock.At(2026, 8, 31, 23, 30))
            .Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void A_month_containing_a_dst_change_still_has_its_calendar_length()
    {
        // Central European DST ends on the last Sunday of October; period maths is date-only,
        // so the month is still 31 days long.
        var resolver = CalendarMonth();

        resolver.Resolve(new DateOnly(2026, 10, 25), PeriodType.Monthly).ToKeyString().Should().Be("2026-M10");
        resolver.Range(PeriodKey.Create(PeriodType.Monthly, 2026, 10).Value).LengthInDays.Should().Be(31);
    }

    [Fact]
    public void A_week_index_that_does_not_exist_in_that_year_is_a_programmer_error()
    {
        // 2026 has 53 ISO weeks; 2027 has 52.
        var resolver = CalendarMonth();

        var act = () => resolver.Range(PeriodKey.Create(PeriodType.Weekly, 2027, 53).Value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_february_period_is_shorter_than_a_march_one()
    {
        var resolver = CalendarMonth();

        resolver.Range(PeriodKey.Create(PeriodType.Monthly, 2027, 2).Value).LengthInDays.Should().Be(28);
        resolver.Range(PeriodKey.Create(PeriodType.Monthly, 2028, 2).Value).LengthInDays.Should().Be(29);
    }

    [Fact]
    public void A_resolver_cannot_be_built_from_a_null_definition()
    {
        var act = () => new PeriodResolver(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TodayIn_rejects_a_null_clock()
    {
        var act = () => CalendarMonth().TodayIn(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
