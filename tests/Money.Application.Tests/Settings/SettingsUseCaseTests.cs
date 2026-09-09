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
    public async Task The_first_Monday_anchor_round_trips_through_the_database()
    {
        var update = new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork);

        (await update.HandleAsync(new UpdateSettingsRequest(
            "EUR", "FirstMonday", 1, "Europe/Budapest", "Monday", 10), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        var read = await new GetSettingsHandler(_harness.Settings).HandleAsync(CancellationToken.None);
        read.PeriodAnchor.Should().Be("FirstMonday");
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

    [Fact]
    public async Task A_fresh_database_reports_no_window_state()
    {
        var state = await new GetWindowStateHandler(_harness.Settings)
            .HandleAsync(CancellationToken.None);

        state.Should().BeNull();
    }

    [Fact]
    public async Task Window_state_round_trips_through_save_then_get()
    {
        var saved = await new SaveWindowStateHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new WindowState(1024, 768, 120, 40), CancellationToken.None);

        saved.IsSuccess.Should().BeTrue();

        var read = await new GetWindowStateHandler(_harness.Settings).HandleAsync(CancellationToken.None);
        read.Should().Be(new WindowState(1024, 768, 120, 40));
    }

    [Fact]
    public async Task Saving_window_state_leaves_the_business_settings_untouched()
    {
        (await new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new UpdateSettingsRequest("HUF", "DayOfMonth", 25, "Europe/Budapest",
                                                   "Sunday", 5), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        await new SaveWindowStateHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new WindowState(800, 600, 10, 20), CancellationToken.None);

        var read = await new GetSettingsHandler(_harness.Settings).HandleAsync(CancellationToken.None);
        read.BaseCurrencyCode.Should().Be("HUF");
        read.PeriodAnchor.Should().Be("DayOfMonth");
        read.PeriodAnchorDay.Should().Be(25);
        read.FirstDayOfWeek.Should().Be("Sunday");
        read.BackupRetentionCount.Should().Be(5);
    }

    [Fact]
    public async Task Saving_the_business_settings_leaves_window_state_untouched()
    {
        await new SaveWindowStateHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new WindowState(800, 600, 10, 20), CancellationToken.None);

        (await new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new UpdateSettingsRequest("HUF", "CalendarMonth", 1, "Europe/Budapest",
                                                   "Monday", 7), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        var read = await new GetWindowStateHandler(_harness.Settings).HandleAsync(CancellationToken.None);
        read.Should().Be(new WindowState(800, 600, 10, 20));
    }

    [Fact]
    public async Task A_window_with_no_size_is_rejected()
    {
        var result = await new SaveWindowStateHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new WindowState(0, 600, 10, 20), CancellationToken.None);

        result.Error!.Code.Should().Be("window.invalid_size");
    }
}
