using Money.Domain.Periods;
using Money.Domain.Time;

namespace Money.Application.Abstractions;

/// <summary>
/// Today's date in the user's configured time zone. The only correct answer to "what is today"
/// anywhere in the application layer - PeriodResolver owns the conversion (spec 5.4).
/// </summary>
public static class TodayResolver
{
    public static async Task<DateOnly> TodayAsync(
        ISettingsRepository settings, IClock clock, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync(cancellationToken);
        var definition = stored?.PeriodDefinition ?? PeriodDefinition.Default;
        return new PeriodResolver(definition).TodayIn(clock);
    }
}
