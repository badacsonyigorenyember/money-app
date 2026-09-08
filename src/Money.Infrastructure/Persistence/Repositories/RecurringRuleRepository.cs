using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Recurrence;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class RecurringRuleRepository(MoneyDbContext context) : IRecurringRuleRepository
{
    public Task<RecurringRule?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.RecurringRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RecurringRule>> ListAsync(
        bool includePaused, CancellationToken cancellationToken = default) =>
        await context.RecurringRules
            .Where(r => includePaused || r.IsActive)
            .OrderBy(r => r.StartDate).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public void Add(RecurringRule rule) => context.RecurringRules.Add(rule);

    public void Remove(RecurringRule rule) => context.RecurringRules.Remove(rule);
}
