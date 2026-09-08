using Money.Application.Abstractions;
using Money.Domain.Accounts;
using Money.Domain.Recurrence;
using Money.Domain.Time;

namespace Money.Application.Recurring;

/// <summary>
/// Posts every occurrence each active rule owes, up to today (spec 5.8). Runs at startup and
/// whenever the transactions screen is opened - both are cheap, because a run that has nothing
/// to do writes nothing.
///
/// Safety rests on three things, in order of strength: the unique index
/// UX_Transactions_Source_Idempotency, the pre-check against dates already posted, and the
/// per-rule watermark. Rent cannot be posted twice.
/// </summary>
public sealed class RecurringMaterialiser(
    IRecurringRuleRepository rules,
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var active = await rules.ListAsync(includePaused: false, cancellationToken);
        if (active.Count == 0) return 0;

        var today = await TodayResolver.TodayAsync(settings, clock, cancellationToken);
        var accountsById = (await accounts.ListAllAsync(cancellationToken)).ToDictionary(a => a.Id);
        var now = clock.UtcNow;
        var posted = 0;

        foreach (var rule in active)
        {
            posted += await MaterialiseAsync(rule, today, accountsById, now, cancellationToken);
        }

        // The watermark moves even when nothing was due, so this saves on every run.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return posted;
    }

    private async Task<int> MaterialiseAsync(
        RecurringRule rule, DateOnly today, IReadOnlyDictionary<Guid, Account> accountsById,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (from, to) = rule.PendingWindow(today);
        if (to < from) return 0;

        var alreadyPosted = (await transactions
            .ExistingRecurringDatesAsync(rule.Id, from, to, cancellationToken)).ToHashSet();

        var posted = 0;

        foreach (var date in ScheduleExpander.Expand(rule.Schedule, rule.StartDate, from, to))
        {
            if (!alreadyPosted.Add(date)) continue;

            var transaction = rule.Materialise(date, accountsById, now);

            // A rule whose account was archived after it was written cannot post. Skipping the
            // occurrence keeps every other rule running; the watermark still advances, so the
            // failure is not retried on every page load.
            if (transaction.IsFailure) continue;

            transactions.Add(transaction.Value);
            posted++;
        }

        rule.MarkMaterialisedThrough(to, now);
        return posted;
    }
}
