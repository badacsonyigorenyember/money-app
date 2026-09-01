using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

public sealed class PeriodDefinitionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(28)]
    public void An_anchor_day_within_one_to_twenty_eight_is_accepted(int day)
    {
        PeriodAnchor.DayOfMonth(day).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(31)]
    public void An_anchor_day_above_twenty_eight_is_rejected_with_an_explanation(int day)
    {
        var result = PeriodAnchor.DayOfMonth(day);

        result.Error!.Code.Should().Be("period.anchor_day_out_of_range");
        result.Error.Message.Should().Contain("not exist in every month");
    }

    [Fact]
    public void The_calendar_month_anchor_reports_day_one()
    {
        PeriodAnchor.CalendarMonth.AnchorDay.Should().Be(1);
        PeriodAnchor.DayOfMonth(25).Value.AnchorDay.Should().Be(25);
    }

    [Fact]
    public void A_definition_requires_a_time_zone_this_machine_knows()
    {
        PeriodDefinition.Create(PeriodAnchor.CalendarMonth, "Mars/Olympus", DayOfWeek.Monday)
            .Error!.Code.Should().Be("period.unknown_time_zone");
    }

    [Fact]
    public void An_iana_time_zone_id_is_accepted()
    {
        PeriodDefinition.Create(PeriodAnchor.CalendarMonth, "Europe/Budapest", DayOfWeek.Monday)
            .IsSuccess.Should().BeTrue();
    }
}
