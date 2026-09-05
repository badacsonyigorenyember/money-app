using Money.Domain.Money;

namespace Money.Application.Abstractions;

/// <summary>
/// The user's base currency, from the settings row saved at first run. The only correct source
/// for "what currency by default" anywhere in the application layer - a hardcoded
/// <see cref="Currency.Eur"/> literal is a bug (see CLAUDE.md C1). Before first-run setup has
/// produced a settings row, EUR is used as a placeholder; no account or category can meaningfully
/// exist yet at that point.
/// </summary>
public static class BaseCurrencyResolver
{
    public static async Task<string> BaseCurrencyCodeAsync(
        ISettingsRepository settings, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync(cancellationToken);
        return stored?.BaseCurrencyCode ?? Currency.Eur.Code;
    }
}
