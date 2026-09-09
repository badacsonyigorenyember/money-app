using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Accounts;

public sealed class CreateAccountHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<AccountDto>> HandleAsync(
        CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = AccountMapper.ParseKind(request.Kind);
        if (kind.IsFailure) return kind.Error!;

        var role = AccountMapper.ParseRole(request.Role);
        if (role.IsFailure) return role.Error!;

        var currencyCode = request.CurrencyCode
            ?? await BaseCurrencyResolver.BaseCurrencyCodeAsync(settings, cancellationToken);
        var currency = Currency.FromCode(currencyCode);
        if (currency.IsFailure) return currency.Error!;

        Account? parent = null;
        if (request.ParentAccountId is { } parentId)
        {
            parent = await accounts.FindAsync(parentId, cancellationToken);
            if (parent is null) return DomainErrors.Account.NotFound(parentId);
        }

        var siblings = await accounts.ChildrenOfAsync(request.ParentAccountId, cancellationToken);
        var slug = Account.Slugify(request.Name ?? "");
        if (siblings.Any(s => string.Equals(Account.Slugify(s.Name), slug, StringComparison.Ordinal)))
            return DomainErrors.Account.DuplicateSiblingName(request.Name ?? "");

        var now = clock.UtcNow;
        var created = Account.Create(
            Guid.CreateVersion7(now), request.Name ?? "", kind.Value, role.Value,
            parent, currency.Value, now);

        if (created.IsFailure) return created.Error!;

        var account = created.Value;
        account.UpdatePresentation(siblings.Count, null, null, null, request.OpenedOn, now);
        accounts.Add(account);

        if (request.OpeningBalance is { } opening && opening != 0m)
        {
            var equity = await EnsureOpeningBalanceAccountAsync(currency.Value, now, cancellationToken);
            if (equity.IsFailure) return equity.Error!;

            var amount = MoneyValue.Of(
                DisplayAmountMapper.ToStored(opening, kind.Value, currency.Value), currency.Value);

            var occurredOn = request.OpenedOn
                ?? await TodayResolver.TodayAsync(settings, clock, cancellationToken);

            var transaction = LedgerTemplates.OpeningBalance(
                Guid.CreateVersion7(now), occurredOn, account, equity.Value, amount, now);

            if (transaction.IsFailure) return transaction.Error!;

            transactions.Add(transaction.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AccountDto>.Ok(AccountMapper.ToDto(account));
    }

    /// <summary>
    /// One opening-balance counterpart per currency. A posting has to match its account's
    /// currency, so a forint account's opening balance cannot be booked against a euro
    /// counterpart - it needs its own, and the currency goes in the name to keep the two apart.
    /// </summary>
    private async Task<Result<Account>> EnsureOpeningBalanceAccountAsync(
        Currency currency, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await accounts.ListAsync(
            AccountKind.Equity, AccountRole.OpeningBalance, includeArchived: true, cancellationToken);

        var match = existing.FirstOrDefault(
            a => string.Equals(a.CurrencyCode, currency.Code, StringComparison.Ordinal));

        if (match is not null) return Result<Account>.Ok(match);

        var name = existing.Count == 0 ? "Opening balance" : $"Opening balance ({currency.Code})";

        var created = Account.Create(
            Guid.CreateVersion7(now), name, AccountKind.Equity,
            AccountRole.OpeningBalance, null, currency, now);

        if (created.IsFailure) return created.Error!;

        accounts.Add(created.Value);
        return created;
    }
}
