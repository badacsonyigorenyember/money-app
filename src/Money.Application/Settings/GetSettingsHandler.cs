using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Periods;

namespace Money.Application.Settings;

public sealed class GetSettingsHandler(ISettingsRepository settings)
{
    public async Task<SettingsDto> HandleAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetAsync(cancellationToken)
            ?? new AppSettings("EUR", PeriodDefinition.Default, 10, false);

        return SettingsMapper.ToDto(stored);
    }
}
