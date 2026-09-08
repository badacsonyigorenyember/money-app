using Money.Domain.Recurrence;

namespace Money.Application.Abstractions;

public interface IRecurringRuleRepository
{
    Task<RecurringRule?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecurringRule>> ListAsync(
        bool includePaused, CancellationToken cancellationToken = default);

    void Add(RecurringRule rule);

    void Remove(RecurringRule rule);
}
