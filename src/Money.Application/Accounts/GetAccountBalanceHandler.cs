using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Presentation;
using Money.Domain.Money;
using Money.Domain.Primitives;

namespace Money.Application.Accounts;

public sealed class GetAccountBalanceHandler(IAccountRepository accounts, ILedgerQueries queries)
{
    public async Task<Result<AccountBalanceDto>> HandleAsync(
        Guid id, DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(id, cancellationToken);
        if (account is null) return DomainErrors.Account.NotFound(id);

        var currency = Currency.FromCode(account.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var minor = await queries.BalanceOfAsync(id, asOf, cancellationToken);

        return Result<AccountBalanceDto>.Ok(new AccountBalanceDto(
            account.Id, account.Name,
            DisplayAmountMapper.ToDisplay(minor, account.Kind, currency.Value),
            account.CurrencyCode, asOf));
    }
}
