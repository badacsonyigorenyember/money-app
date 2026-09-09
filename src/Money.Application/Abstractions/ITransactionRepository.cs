using Money.Domain.Ledger;

namespace Money.Application.Abstractions;

public interface ITransactionRepository
{
    Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transaction>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Which dates this rule has already posted on, voided ones included. The cheap pre-check
    /// that keeps a second materialiser run quiet; UX_Transactions_Source_Idempotency is the
    /// guard that makes it correct.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> ExistingRecurringDatesAsync(
        Guid ruleId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    void Add(Transaction transaction);
}
