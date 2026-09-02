using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Periods;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class SettingsRepository(MoneyDbContext context) : ISettingsRepository
{
    // The two persisted anchor names. Task 24's SettingsMapper exposes the same two strings to
    // the API, and SettingsUseCaseTests round-trips them; if these ever disagree, that test fails.
    internal const string CalendarMonthAnchorName = "CalendarMonth";
    internal const string DayOfMonthAnchorName = "DayOfMonth";

    public async Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.Settings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (row is null) return null;

        var anchor = row.PeriodAnchor == DayOfMonthAnchorName
            ? PeriodAnchor.DayOfMonth(row.PeriodAnchorDay).Value
            : PeriodAnchor.CalendarMonth;

        var definition = PeriodDefinition.Create(
            anchor, row.TimeZoneId, Enum.Parse<DayOfWeek>(row.FirstDayOfWeek)).Value;

        return new AppSettings(
            row.BaseCurrencyCode, definition, row.BackupRetentionCount, row.FirstRunCompleted);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var row = await context.Settings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        var isNew = row is null;
        row ??= new SettingsEntity { Id = 1 };

        row.BaseCurrencyCode = settings.BaseCurrencyCode;
        row.PeriodAnchor = settings.PeriodDefinition.Anchor is PeriodAnchor.DayOfMonthAnchor
            ? DayOfMonthAnchorName
            : CalendarMonthAnchorName;
        row.PeriodAnchorDay = settings.PeriodDefinition.Anchor.AnchorDay;
        row.TimeZoneId = settings.PeriodDefinition.TimeZoneId;
        row.FirstDayOfWeek = settings.PeriodDefinition.FirstDayOfWeek.ToString();
        row.BackupRetentionCount = settings.BackupRetentionCount;
        row.FirstRunCompleted = settings.FirstRunCompleted;

        if (isNew) context.Settings.Add(row);
    }
}
