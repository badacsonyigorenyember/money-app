using Money.Domain.Ledger;

namespace Money.Application.Abstractions;

public interface ITransactionRepository
{
    Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transaction>> ListAllAsync(CancellationToken cancellationToken = default);

    void Add(Transaction transaction);
}
