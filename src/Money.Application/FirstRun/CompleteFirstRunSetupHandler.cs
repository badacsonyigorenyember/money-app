using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.FirstRun;

/// <summary>
/// The whole of first run in one transaction: settings, the first account with its opening
/// balance, and the starter category tree. Everything validates before anything is written, so a
/// rejected anchor day leaves an empty database rather than a half-built one.
/// </summary>
public sealed class CompleteFirstRunSetupHandler(
    ISettingsRepository settings,
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<SettingsDto>> HandleAsync(
        FirstRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await settings.GetAsync(cancellationToken);
        if (existing?.FirstRunCompleted == true) return DomainErrors.Settings.AlreadyInitialised();

        var currencyCode = SettingsMapper.ValidateCurrency(request.BaseCurrencyCode);
        if (currencyCode.IsFailure) return currencyCode.Error!;

        var definition = SettingsMapper.BuildDefinition(
            request.PeriodAnchor, request.PeriodAnchorDay, request.TimeZoneId, request.FirstDayOfWeek);
        if (definition.IsFailure) return definition.Error!;

        var role = AccountMapper.ParseRole(request.FirstAccountRole);
        if (role.IsFailure) return role.Error!;
        if (role.Value is not (AccountRole.Bank or AccountRole.Cash))
            return DomainErrors.Account.KindRoleMismatch(nameof(AccountKind.Asset), request.FirstAccountRole);

        var currency = Currency.FromCode(currencyCode.Value).Value;
        var now = clock.UtcNow;

        var firstAccount = Account.Create(
            Guid.CreateVersion7(now), request.FirstAccountName, AccountKind.Asset, role.Value,
            null, currency, now);
        if (firstAccount.IsFailure) return firstAccount.Error!;

        var openingEquity = Account.Create(
            Guid.CreateVersion7(now), "Opening balance", AccountKind.Equity,
            AccountRole.OpeningBalance, null, currency, now);
        if (openingEquity.IsFailure) return openingEquity.Error!;

        Transaction? opening = null;
        if (request.OpeningBalance != 0m)
        {
            var amount = MoneyValue.Of(
                MoneyValue.RoundToMinor(request.OpeningBalance * currency.MinorUnitScale), currency);

            var built = LedgerTemplates.OpeningBalance(
                Guid.CreateVersion7(now), request.OpenedOn,
                firstAccount.Value, openingEquity.Value, amount, now);

            if (built.IsFailure) return built.Error!;
            opening = built.Value;
        }

        // Nothing has been written yet. From here on, every step is known to succeed.
        firstAccount.Value.UpdatePresentation(0, null, null, null, request.OpenedOn, now);
        accounts.Add(firstAccount.Value);
        accounts.Add(openingEquity.Value);
        if (opening is not null) transactions.Add(opening);

        if (request.SeedStarterCategories)
        {
            SeedCategories(StarterCategories.Expense, AccountKind.Expense, currency, now);
            SeedCategories(StarterCategories.Income, AccountKind.Income, currency, now);
        }

        var saved = new AppSettings(currencyCode.Value, definition.Value, 10, FirstRunCompleted: true);
        await settings.SaveAsync(saved, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SettingsDto>.Ok(SettingsMapper.ToDto(saved));
    }

    private void SeedCategories(
        IReadOnlyList<string> names, AccountKind kind, Currency currency, DateTimeOffset now)
    {
        for (var i = 0; i < names.Count; i++)
        {
            var created = Account.Create(
                Guid.CreateVersion7(now), names[i], kind, AccountRole.Category, null, currency, now);

            if (created.IsFailure) continue;

            created.Value.UpdatePresentation(i, null, null, null, null, now);
            accounts.Add(created.Value);
        }
    }
}
