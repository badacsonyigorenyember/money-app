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

namespace Money.Application.Transactions;

public sealed class TransferHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        TransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var from = await accounts.FindAsync(request.FromAccountId, cancellationToken);
        if (from is null) return DomainErrors.Account.NotFound(request.FromAccountId);

        var to = await accounts.FindAsync(request.ToAccountId, cancellationToken);
        if (to is null) return DomainErrors.Account.NotFound(request.ToAccountId);

        if (from.Id == to.Id) return DomainErrors.Transaction.DuplicateAccount(from.Name);

        var currency = Currency.FromCode(from.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var amount = MoneyValue.Of(
            DisplayAmountMapper.ToStored(request.Amount, AccountKind.Asset, currency.Value), currency.Value);

        var occurredOn = request.OccurredOn
            ?? await TodayResolver.TodayAsync(settings, clock, cancellationToken);

        var now = clock.UtcNow;
        var created = LedgerTemplates.Transfer(
            Guid.CreateVersion7(now), occurredOn,
            string.IsNullOrWhiteSpace(request.Description)
                ? $"{from.Name} to {to.Name}"
                : request.Description.Trim(),
            from, to, amount, now);

        if (created.IsFailure) return created.Error!;

        transactions.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var byId = new Dictionary<Guid, Account> { [from.Id] = from, [to.Id] = to };
        return Result<TransactionDto>.Ok(TransactionMapper.ToDto(created.Value, byId));
    }
}
