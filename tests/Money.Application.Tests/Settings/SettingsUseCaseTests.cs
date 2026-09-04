using Money.Application.Contracts;
using Money.Application.Settings;

namespace Money.Application.Tests.Settings;

public sealed class SettingsUseCaseTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Before_first_run_the_defaults_are_reported()
    {
        var settings = await new GetSettingsHandler(_harness.Settings)
            .HandleAsync(CancellationToken.None);

        settings.BaseCurrencyCode.Should().Be("EUR");
        settings.FirstRunCompleted.Should().BeFalse();
        settings.PeriodAnchor.Should().Be("CalendarMonth");
        settings.PeriodAnchorDay.Should().Be(1);
    }

    [Fact]
    public async Task Settings_round_trip_through_the_database()
    {
        var update = new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork);

        var saved = await update.HandleAsync(new UpdateSettingsRequest(
            "HUF", "DayOfMonth", 25, "Europe/Budapest", "Sunday", 5), CancellationToken.None);

        saved.IsSuccess.Should().BeTrue();

        var read = await new GetSettingsHandler(_harness.Settings).HandleAsync(CancellationToken.None);
        read.BaseCurrencyCode.Should().Be("HUF");
        read.PeriodAnchor.Should().Be("DayOfMonth");
        read.PeriodAnchorDay.Should().Be(25);
        read.FirstDayOfWeek.Should().Be("Sunday");
        read.BackupRetentionCount.Should().Be(5);
    }

    [Fact]
    public async Task An_anchor_day_above_twenty_eight_is_rejected_with_the_explanation()
    {
        var result = await new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new UpdateSettingsRequest("EUR", "DayOfMonth", 31, "Europe/Budapest",
                                                   "Monday", 10), CancellationToken.None);

        result.Error!.Code.Should().Be("period.anchor_day_out_of_range");
        result.Error.Message.Should().Contain("not exist in every month");
    }

    [Fact]
    public async Task An_unknown_time_zone_is_rejected()
    {
        (await new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new UpdateSettingsRequest("EUR", "CalendarMonth", 1, "Mars/Olympus",
                                                   "Monday", 10), CancellationToken.None))
            .Error!.Code.Should().Be("period.unknown_time_zone");
    }

    [Fact]
    public async Task An_unknown_currency_is_rejected()
    {
        (await new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new UpdateSettingsRequest("XYZ", "CalendarMonth", 1, "Europe/Budapest",
                                                   "Monday", 10), CancellationToken.None))
            .Error!.Code.Should().Be("currency.unknown");
    }
}
