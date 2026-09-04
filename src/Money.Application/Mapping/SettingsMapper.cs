using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Domain.Money;
using Money.Domain.Periods;
using Money.Domain.Primitives;

namespace Money.Application.Mapping;

public static class SettingsMapper
{
    public const string CalendarMonthAnchorName = "CalendarMonth";
    public const string DayOfMonthAnchorName = "DayOfMonth";

    public static SettingsDto ToDto(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var anchor = settings.PeriodDefinition.Anchor is PeriodAnchor.DayOfMonthAnchor
            ? DayOfMonthAnchorName
            : CalendarMonthAnchorName;

        return new SettingsDto(
            settings.BaseCurrencyCode, anchor, settings.PeriodDefinition.Anchor.AnchorDay,
            settings.PeriodDefinition.TimeZoneId, settings.PeriodDefinition.FirstDayOfWeek.ToString(),
            settings.BackupRetentionCount, settings.FirstRunCompleted);
    }

    public static Result<PeriodDefinition> BuildDefinition(
        string? anchorName, int anchorDay, string? timeZoneId, string? firstDayOfWeek)
    {
        Result<PeriodAnchor> anchor;

        if (string.Equals(anchorName, DayOfMonthAnchorName, StringComparison.OrdinalIgnoreCase))
        {
            anchor = PeriodAnchor.DayOfMonth(anchorDay);
            if (anchor.IsFailure) return anchor.Error!;
        }
        else
        {
            anchor = Result<PeriodAnchor>.Ok(PeriodAnchor.CalendarMonth);
        }

        if (!Enum.TryParse<DayOfWeek>(firstDayOfWeek, ignoreCase: true, out var day))
            day = DayOfWeek.Monday;

        return PeriodDefinition.Create(anchor.Value, timeZoneId ?? "", day);
    }

    public static Result<string> ValidateCurrency(string? code) =>
        Currency.FromCode(code ?? "").Map(c => c.Code);
}
