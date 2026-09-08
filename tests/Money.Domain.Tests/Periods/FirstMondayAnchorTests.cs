using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

/// <summary>
/// The first-Monday anchor is the one whose boundary is not a fixed day number: it moves between
/// the 1st and the 7th. These pin where it actually lands, and PeriodTilingPropertyTests proves
/// the moving boundary still tiles (I9).
/// </summary>
public sealed class FirstMondayAnchorTests
{
    private static PeriodResolver Resolver() =>
        new(PeriodDefinition.Create(PeriodAnchor.FirstMonday, "Europe/Budapest", DayOfWeek.Monday).Value);

    [Fact]
    public void A_month_runs_from_the_first_Monday_to_the_next_first_Monday()
    {
        // 2026: 7 September is the first Monday, 5 October is the next one.
        var range = Resolver().Range(PeriodKey.Create(PeriodType.Monthly, 2026, 9).Value);

        range.Start.Should().Be(new DateOnly(2026, 9, 7));
        range.EndExclusive.Should().Be(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public void A_month_that_opens_on_a_Monday_starts_on_the_first()
    {
        // 1 June 2026 is itself a Monday, so the anchor is the 1st and not the 8th.
        Resolver().Range(PeriodKey.Create(PeriodType.Monthly, 2026, 6).Value)
                  .Start.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void Days_before_the_first_Monday_still_belong_to_the_month_before()
    {
        var resolver = Resolver();

        resolver.Resolve(new DateOnly(2026, 9, 6), PeriodType.Monthly).ToKeyString().Should().Be("2026-M08");
        resolver.Resolve(new DateOnly(2026, 9, 7), PeriodType.Monthly).ToKeyString().Should().Be("2026-M09");
        resolver.Resolve(new DateOnly(2026, 10, 4), PeriodType.Monthly).ToKeyString().Should().Be("2026-M09");
    }

    [Fact]
    public void A_quarter_starts_on_the_first_Monday_of_its_first_month()
    {
        // Q4 2026: 5 October is the first Monday of October, 4 January 2027 of January.
        var range = Resolver().Range(PeriodKey.Create(PeriodType.Quarterly, 2026, 4).Value);

        range.Start.Should().Be(new DateOnly(2026, 10, 5));
        range.EndExclusive.Should().Be(new DateOnly(2027, 1, 4));
    }

    [Fact]
    public void A_year_starts_on_the_first_Monday_of_January()
    {
        var range = Resolver().Range(PeriodKey.Create(PeriodType.Yearly, 2026, 1).Value);

        range.Start.Should().Be(new DateOnly(2026, 1, 5));
        range.EndExclusive.Should().Be(new DateOnly(2027, 1, 4));
    }
}
