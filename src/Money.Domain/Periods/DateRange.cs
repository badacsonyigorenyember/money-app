using Money.Domain.Primitives;

namespace Money.Domain.Periods;

/// <summary>A half-open date interval: [Start, EndExclusive).</summary>
public readonly record struct DateRange
{
    private DateRange(DateOnly start, DateOnly endExclusive)
    {
        Start = start;
        EndExclusive = endExclusive;
    }

    public DateOnly Start { get; }

    public DateOnly EndExclusive { get; }

    public int LengthInDays => EndExclusive.DayNumber - Start.DayNumber;

    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;

    public static Result<DateRange> Create(DateOnly start, DateOnly endExclusive) =>
        endExclusive > start
            ? Result<DateRange>.Ok(new DateRange(start, endExclusive))
            : DomainErrors.Period.EndBeforeStart(start, endExclusive);

    /// <summary>For callers that have already established ordering (PeriodResolver).</summary>
    internal static DateRange FromOrdered(DateOnly start, DateOnly endExclusive) => new(start, endExclusive);

    public override string ToString() => $"[{Start:O}, {EndExclusive:O})";
}
