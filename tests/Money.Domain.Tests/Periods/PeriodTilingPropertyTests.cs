using CsCheck;
using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

/// <summary>
/// Invariant I9: periods of one PeriodType tile the timeline - no gaps, no overlaps.
/// </summary>
public sealed class PeriodTilingPropertyTests
{
    private static readonly Gen<DateOnly> AnyDate =
        Gen.Int[new DateOnly(2000, 1, 1).DayNumber, new DateOnly(2060, 12, 31).DayNumber]
           .Select(DateOnly.FromDayNumber);

    private static readonly Gen<PeriodType> AnyType =
        Gen.OneOfConst(PeriodType.Weekly, PeriodType.Monthly, PeriodType.Quarterly, PeriodType.Yearly);

    /// <summary>
    /// Every anchor the settings screen can produce, including the first-Monday one whose boundary
    /// moves between the 1st and the 7th - a moving boundary is exactly the case where tiling is
    /// not obvious, so it belongs in the property, not only in an example test.
    /// </summary>
    private static readonly Gen<PeriodAnchor> AnyAnchor = Gen.Int[0, 29].Select(n => n switch
    {
        0 => PeriodAnchor.CalendarMonth,
        1 => PeriodAnchor.FirstMonday,
        _ => PeriodAnchor.DayOfMonth(n - 1).Value
    });

    private static readonly Gen<DayOfWeek> AnyFirstDayOfWeek =
        Gen.OneOfConst(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                       DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday);

    private static PeriodResolver ResolverFor(PeriodAnchor anchor, DayOfWeek firstDayOfWeek) =>
        new(PeriodDefinition.Create(anchor, "Europe/Budapest", firstDayOfWeek).Value);

    [Fact]
    public void Every_date_falls_inside_the_period_it_resolves_to()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchor, AnyFirstDayOfWeek)
           .Sample((date, type, anchor, firstDay) =>
           {
               var resolver = ResolverFor(anchor, firstDay);
               return resolver.Range(resolver.Resolve(date, type)).Contains(date);
           }, iter: 10_000);
    }

    [Fact]
    public void Consecutive_periods_share_a_boundary_exactly()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchor, AnyFirstDayOfWeek)
           .Sample((date, type, anchor, firstDay) =>
           {
               var resolver = ResolverFor(anchor, firstDay);
               var key = resolver.Resolve(date, type);
               return resolver.Range(resolver.Next(key)).Start == resolver.Range(key).EndExclusive;
           }, iter: 10_000);
    }

    [Fact]
    public void Previous_undoes_next()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchor, AnyFirstDayOfWeek)
           .Sample((date, type, anchor, firstDay) =>
           {
               var resolver = ResolverFor(anchor, firstDay);
               var key = resolver.Resolve(date, type);
               return resolver.Previous(resolver.Next(key)) == key;
           }, iter: 10_000);
    }

    [Fact]
    public void The_day_before_a_period_belongs_to_the_previous_period_and_no_other()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchor, AnyFirstDayOfWeek)
           .Sample((date, type, anchor, firstDay) =>
           {
               var resolver = ResolverFor(anchor, firstDay);
               var key = resolver.Resolve(date, type);
               var dayBefore = resolver.Range(key).Start.AddDays(-1);
               return resolver.Resolve(dayBefore, type) == resolver.Previous(key);
           }, iter: 10_000);
    }
}
