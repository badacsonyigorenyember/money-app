using Money.Application.Contracts;
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Money;
using Money.Domain.Recurrence;

namespace Money.Application.Mapping;

public static class RecurringRuleMapper
{
    public static RecurringRuleDto ToDto(
        RecurringRule rule, IReadOnlyDictionary<Guid, Account> accountsById, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(accountsById);

        var currency = Currency.FromCode(rule.CurrencyCode).Value;

        // The stored amount is a magnitude - both sides of the rule carry it, one negated - so it
        // is shown as-is. DisplayAmountMapper's sign flip belongs to a posting, not to a rule.
        var amount = rule.AmountMinor / currency.MinorUnitScale;

        var next = rule.IsActive
            ? ScheduleExpander
                .Expand(rule.Schedule, rule.StartDate, today, rule.EndDate ?? DateOnly.MaxValue)
                .Cast<DateOnly?>()
                .FirstOrDefault()
            : null;

        return new RecurringRuleDto(
            rule.Id, rule.Description, rule.Payee, amount, rule.CurrencyCode,
            Name(accountsById, rule.DebitAccountId), Name(accountsById, rule.CreditAccountId),
            ScheduleDescription.Describe(rule.Schedule),
            rule.StartDate, rule.EndDate, rule.IsActive, next);
    }

    private static string Name(IReadOnlyDictionary<Guid, Account> accountsById, Guid id) =>
        accountsById.TryGetValue(id, out var account)
            ? AccountDisplayName.For(account.Name, account.IsDeleted)
            : "(missing)";
}
