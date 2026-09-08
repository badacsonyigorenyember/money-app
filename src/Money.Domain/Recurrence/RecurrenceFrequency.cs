namespace Money.Domain.Recurrence;

/// <summary>
/// How often a rule repeats. <see cref="Custom"/> is the escape hatch the other four cannot
/// express: a step of N years, M months and D days combined.
/// </summary>
public enum RecurrenceFrequency
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4,
    Custom = 5
}
