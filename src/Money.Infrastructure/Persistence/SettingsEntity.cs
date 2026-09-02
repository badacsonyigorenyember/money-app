namespace Money.Infrastructure.Persistence;

/// <summary>The single settings row. Id is always 1.</summary>
public sealed class SettingsEntity
{
    public int Id { get; set; } = 1;
    public string BaseCurrencyCode { get; set; } = "EUR";
    public string PeriodAnchor { get; set; } = "CalendarMonth";
    public int PeriodAnchorDay { get; set; } = 1;
    public string TimeZoneId { get; set; } = "Europe/Budapest";
    public string FirstDayOfWeek { get; set; } = nameof(DayOfWeek.Monday);
    public int BackupRetentionCount { get; set; } = 10;
    public bool FirstRunCompleted { get; set; }

    // Reserved for phase 8's window-state persistence; unused until then.
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
