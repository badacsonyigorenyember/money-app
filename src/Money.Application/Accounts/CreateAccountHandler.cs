using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
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

        var currency = Currency.FromCode(request.CurrencyCode ?? Currency.Eur.Code);
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
                MoneyValue.RoundToMinor(opening * currency.Value.MinorUnitScale), currency.Value);

            var transaction = LedgerTemplates.OpeningBalance(
                Guid.CreateVersion7(now),
                request.OpenedOn ?? DateOnly.FromDateTime(now.UtcDateTime),
                account, equity.Value, amount, now);

            if (transaction.IsFailure) return transaction.Error!;

            transactions.Add(transaction.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AccountDto>.Ok(AccountMapper.ToDto(account));
    }

    private async Task<Result<Account>> EnsureOpeningBalanceAccountAsync(
        Currency currency, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await accounts.FindFirstByRoleAsync(AccountRole.OpeningBalance, cancellationToken);
        if (existing is not null) return Result<Account>.Ok(existing);

        var created = Account.Create(
            Guid.CreateVersion7(now), "Opening balance", AccountKind.Equity,
            AccountRole.OpeningBalance, null, currency, now);

        if (created.IsFailure) return created.Error!;

        accounts.Add(created.Value);
        return created;
    }
}
