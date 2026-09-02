using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Ledger;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class TransactionRepository(MoneyDbContext context) : ITransactionRepository
{
    public Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Transactions.Include(t => t.Postings)
                            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        await context.Transactions.Include(t => t.Postings)
                                  .OrderBy(t => t.OccurredOn).ThenBy(t => t.Id)
                                  .ToListAsync(cancellationToken);

    public void Add(Transaction transaction) => context.Transactions.Add(transaction);
}
