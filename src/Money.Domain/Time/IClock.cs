namespace Money.Domain.Time;

/// <summary>
/// The only source of time in the solution. Ambient time is banned by an architecture test.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
