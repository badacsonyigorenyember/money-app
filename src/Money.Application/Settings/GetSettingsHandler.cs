using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;

namespace Money.Application.Settings;

public sealed class GetSettingsHandler(ISettingsRepository settings)
{
    public async Task<SettingsDto> HandleAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetAsync(cancellationToken) ?? AppSettings.Default;

        return SettingsMapper.ToDto(stored);
    }
}
