using Money.Domain.Time;

namespace Money.TestSupport;

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }

    public static FakeClock At(int year, int month, int day) =>
        new(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));

    public static FakeClock At(int year, int month, int day, int hour, int minute) =>
        new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
