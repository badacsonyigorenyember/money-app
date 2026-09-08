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

/// <summary>
/// One line in, one balanced transaction out. Which way the money went is read off the chosen
/// category's own Kind rather than asked for separately: a user picking "Salary" has already
/// said it is income, and picking "Food" has already said it is a spend.
/// </summary>
public sealed class QuickEntryHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        QuickEntryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = await accounts.FindAsync(request.CategoryId, cancellationToken);
        if (category is null) return DomainErrors.Account.NotFound(request.CategoryId);

        var account = await accounts.FindAsync(request.AccountId, cancellationToken);
        if (account is null) return DomainErrors.Account.NotFound(request.AccountId);

        var currency = Currency.FromCode(account.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        // A template takes a magnitude. AccountKind.Expense is the kind DisplayAmountMapper leaves
        // alone, so converting through it yields the positive minor units both templates want.
        var amount = MoneyValue.Of(
            DisplayAmountMapper.ToStored(request.Amount, AccountKind.Expense, currency.Value),
            currency.Value);

        var occurredOn = request.OccurredOn
            ?? await TodayResolver.TodayAsync(settings, clock, cancellationToken);

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? category.Name
            : request.Description.Trim();

        var now = clock.UtcNow;

        var created = category.Kind == AccountKind.Income
            ? LedgerTemplates.Income(
                Guid.CreateVersion7(now), occurredOn, description, request.Payee,
                receivedInto: account, category: category, amount, now)
            : LedgerTemplates.Expense(
                Guid.CreateVersion7(now), occurredOn, description, request.Payee,
                paidFrom: account, category: category, amount, now);

        if (created.IsFailure) return created.Error!;

        transactions.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var byId = new Dictionary<Guid, Account> { [category.Id] = category, [account.Id] = account };
        return Result<TransactionDto>.Ok(TransactionMapper.ToDto(created.Value, byId));
    }
}
