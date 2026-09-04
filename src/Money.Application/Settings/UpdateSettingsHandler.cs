using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Periods;
using Money.Domain.Primitives;

namespace Money.Application.Settings;

public sealed class UpdateSettingsHandler(ISettingsRepository settings, IUnitOfWork unitOfWork)
{
    public async Task<Result<SettingsDto>> HandleAsync(
        UpdateSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currency = SettingsMapper.ValidateCurrency(request.BaseCurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var definition = SettingsMapper.BuildDefinition(
            request.PeriodAnchor, request.PeriodAnchorDay, request.TimeZoneId, request.FirstDayOfWeek);
        if (definition.IsFailure) return definition.Error!;

        var existing = await settings.GetAsync(cancellationToken);

        var updated = new AppSettings(
            currency.Value, definition.Value,
            Math.Clamp(request.BackupRetentionCount, 1, 100),
            existing?.FirstRunCompleted ?? false);

        await settings.SaveAsync(updated, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SettingsDto>.Ok(SettingsMapper.ToDto(updated));
    }
}
