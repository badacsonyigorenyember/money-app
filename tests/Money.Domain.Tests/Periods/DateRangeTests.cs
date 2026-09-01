using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

public sealed class DateRangeTests
{
    [Fact]
    public void A_range_includes_its_start_and_excludes_its_end()
    {
        var range = DateRange.Create(new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 25)).Value;

        range.Contains(new DateOnly(2026, 9, 25)).Should().BeTrue();
        range.Contains(new DateOnly(2026, 10, 24)).Should().BeTrue();
        range.Contains(new DateOnly(2026, 10, 25)).Should().BeFalse();
        range.Contains(new DateOnly(2026, 9, 24)).Should().BeFalse();
    }

    [Fact]
    public void A_range_reports_its_length_in_days()
    {
        DateRange.Create(new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 25)).Value
            .LengthInDays.Should().Be(30);
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_rejected()
    {
        DateRange.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1))
            .Error!.Code.Should().Be("period.end_before_start");
    }
}
