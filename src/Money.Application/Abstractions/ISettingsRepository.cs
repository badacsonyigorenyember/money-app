using Money.Domain.Periods;

namespace Money.Application.Abstractions;

/// <summary>The settings row, as the application layer sees it.</summary>
public sealed record AppSettings(
    string BaseCurrencyCode,
    PeriodDefinition PeriodDefinition,
    int BackupRetentionCount,
    bool FirstRunCompleted);

public interface ISettingsRepository
{
    Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
