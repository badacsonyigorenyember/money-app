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

    private static readonly Gen<int> AnyAnchorDay = Gen.Int[1, 28];

    private static readonly Gen<DayOfWeek> AnyFirstDayOfWeek =
        Gen.OneOfConst(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                       DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday);

    private static PeriodResolver ResolverFor(int anchorDay, DayOfWeek firstDayOfWeek) =>
        new(PeriodDefinition.Create(
                PeriodAnchor.DayOfMonth(anchorDay).Value, "Europe/Budapest", firstDayOfWeek).Value);

    [Fact]
    public void Every_date_falls_inside_the_period_it_resolves_to()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchorDay, AnyFirstDayOfWeek)
           .Sample((date, type, anchorDay, firstDay) =>
           {
               var resolver = ResolverFor(anchorDay, firstDay);
               return resolver.Range(resolver.Resolve(date, type)).Contains(date);
           }, iter: 10_000);
    }

    [Fact]
    public void Consecutive_periods_share_a_boundary_exactly()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchorDay, AnyFirstDayOfWeek)
           .Sample((date, type, anchorDay, firstDay) =>
           {
               var resolver = ResolverFor(anchorDay, firstDay);
               var key = resolver.Resolve(date, type);
               return resolver.Range(resolver.Next(key)).Start == resolver.Range(key).EndExclusive;
           }, iter: 10_000);
    }

    [Fact]
    public void Previous_undoes_next()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchorDay, AnyFirstDayOfWeek)
           .Sample((date, type, anchorDay, firstDay) =>
           {
               var resolver = ResolverFor(anchorDay, firstDay);
               var key = resolver.Resolve(date, type);
               return resolver.Previous(resolver.Next(key)) == key;
           }, iter: 10_000);
    }

    [Fact]
    public void The_day_before_a_period_belongs_to_the_previous_period_and_no_other()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchorDay, AnyFirstDayOfWeek)
           .Sample((date, type, anchorDay, firstDay) =>
           {
               var resolver = ResolverFor(anchorDay, firstDay);
               var key = resolver.Resolve(date, type);
               var dayBefore = resolver.Range(key).Start.AddDays(-1);
               return resolver.Resolve(dayBefore, type) == resolver.Previous(key);
           }, iter: 10_000);
    }
}
