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

    public async Task<IReadOnlyList<string>> ExistingExternalRefsAsync(
        IReadOnlyCollection<string> externalRefs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalRefs);
        if (externalRefs.Count == 0) return [];

        // Voided imports count as present: re-importing a line the user deliberately removed
        // would undo that removal on the next sync.
        return await context.Transactions
            .Where(t => t.ExternalRef != null && externalRefs.Contains(t.ExternalRef))
            .Select(t => t.ExternalRef!)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DateOnly>> ExistingRecurringDatesAsync(
        Guid ruleId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default) =>
        // Voided occurrences count as present: re-posting one the user deliberately removed would
        // undo that removal on the next run.
        await context.Transactions
            .Where(t => t.SourceKind == TransactionSourceKind.Recurring
                        && t.SourceId == ruleId
                        && t.OccurredOn >= fromDate && t.OccurredOn <= toDate)
            .Select(t => t.OccurredOn)
            .ToListAsync(cancellationToken);

    public void Add(Transaction transaction) => context.Transactions.Add(transaction);
}
