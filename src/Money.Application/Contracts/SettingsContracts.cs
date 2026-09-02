namespace Money.Application.Contracts;

public sealed record SettingsDto(
    string BaseCurrencyCode, string PeriodAnchor, int PeriodAnchorDay,
    string TimeZoneId, string FirstDayOfWeek, int BackupRetentionCount, bool FirstRunCompleted);

public sealed record UpdateSettingsRequest(
    string BaseCurrencyCode, string PeriodAnchor, int PeriodAnchorDay,
    string TimeZoneId, string FirstDayOfWeek, int BackupRetentionCount);

public sealed record FirstRunRequest(
    string BaseCurrencyCode, string PeriodAnchor, int PeriodAnchorDay,
    string TimeZoneId, string FirstDayOfWeek,
    string FirstAccountName, string FirstAccountRole, decimal OpeningBalance, DateOnly OpenedOn,
    bool SeedStarterCategories);
