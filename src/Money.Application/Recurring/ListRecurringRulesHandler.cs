using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Time;

namespace Money.Application.Recurring;

public sealed class ListRecurringRulesHandler(
    IRecurringRuleRepository rules,
    IAccountRepository accounts,
    ISettingsRepository settings,
    IClock clock)
{
    public async Task<IReadOnlyList<RecurringRuleDto>> HandleAsync(
        bool includePaused = true, CancellationToken cancellationToken = default)
    {
        var found = await rules.ListAsync(includePaused, cancellationToken);
        if (found.Count == 0) return [];

        var byId = (await accounts.ListAllAsync(cancellationToken)).ToDictionary(a => a.Id);
        var today = await TodayResolver.TodayAsync(settings, clock, cancellationToken);

        return found.Select(rule => RecurringRuleMapper.ToDto(rule, byId, today)).ToArray();
    }
}
