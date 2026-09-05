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

public sealed class QuickExpenseHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        QuickExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = await accounts.FindAsync(request.CategoryId, cancellationToken);
        if (category is null) return DomainErrors.Account.NotFound(request.CategoryId);

        var paidFrom = await accounts.FindAsync(request.AccountId, cancellationToken);
        if (paidFrom is null) return DomainErrors.Account.NotFound(request.AccountId);

        var currency = Currency.FromCode(paidFrom.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var amount = MoneyValue.Of(
            DisplayAmountMapper.ToStored(request.Amount, AccountKind.Expense, currency.Value), currency.Value);

        var occurredOn = request.OccurredOn
            ?? await TodayResolver.TodayAsync(settings, clock, cancellationToken);

        var now = clock.UtcNow;
        var created = LedgerTemplates.Expense(
            Guid.CreateVersion7(now), occurredOn,
            string.IsNullOrWhiteSpace(request.Description) ? category.Name : request.Description.Trim(),
            request.Payee, paidFrom, category, amount, now);

        if (created.IsFailure) return created.Error!;

        transactions.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var byId = new Dictionary<Guid, Account> { [category.Id] = category, [paidFrom.Id] = paidFrom };
        return Result<TransactionDto>.Ok(TransactionMapper.ToDto(created.Value, byId));
    }
}
