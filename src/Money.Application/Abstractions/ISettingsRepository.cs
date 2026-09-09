using Money.Domain.Periods;

namespace Money.Application.Abstractions;

/// <summary>
/// The settings row, as the application layer sees it. The four window fields are the desktop
/// host's last window rectangle; they are null until it has saved one, and are never exposed
/// over HTTP or on the Settings screen.
/// </summary>
public sealed record AppSettings(
    string BaseCurrencyCode,
    PeriodDefinition PeriodDefinition,
    int BackupRetentionCount,
    bool FirstRunCompleted,
    int? WindowWidth = null,
    int? WindowHeight = null,
    int? WindowX = null,
    int? WindowY = null)
{
    /// <summary>What a database with no settings row reads as.</summary>
    public static AppSettings Default { get; } =
        new("EUR", PeriodDefinition.Default, 10, FirstRunCompleted: false);
}

public interface ISettingsRepository
{
    Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
