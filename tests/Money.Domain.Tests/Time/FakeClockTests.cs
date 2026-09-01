using Money.TestSupport;

namespace Money.Domain.Tests.Time;

public sealed class FakeClockTests
{
    [Fact]
    public void A_fake_clock_holds_still_until_advanced()
    {
        var clock = FakeClock.At(2026, 9, 1);

        var first = clock.UtcNow;
        var second = clock.UtcNow;

        second.Should().Be(first);
        first.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Advancing_moves_the_clock_by_exactly_the_requested_amount()
    {
        var clock = FakeClock.At(2026, 9, 1);

        clock.Advance(TimeSpan.FromHours(36));

        clock.UtcNow.Should().Be(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
    }
}
