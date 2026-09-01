using Money.Domain.Time;

namespace Money.Infrastructure.Time;

/// <summary>
/// Composition root. This file is on the architecture test's allow-list for ambient time;
/// nothing else in src/ may read the system clock.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
