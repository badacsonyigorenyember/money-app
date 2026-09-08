using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Recurrence;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Recurring;

public sealed class CreateRecurringRuleHandler(
    IAccountRepository accounts,
    IRecurringRuleRepository rules,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<RecurringRuleDto>> HandleAsync(
        CreateRecurringRuleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await accounts.FindAsync(request.AccountId, cancellationToken);
        if (account is null) return DomainErrors.Account.NotFound(request.AccountId);

        var counterparty = await accounts.FindAsync(request.CounterpartyId, cancellationToken);
        if (counterparty is null) return DomainErrors.Account.NotFound(request.CounterpartyId);

        var currency = Currency.FromCode(account.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        // A template takes a magnitude, not a signed amount - which is why AccountKind.Expense,
        // the kind DisplayAmountMapper leaves alone, is the right kind to convert through here
        // for all three directions. QuickEntryHandler converts the same way.
        var amount = MoneyValue.Of(
            DisplayAmountMapper.ToStored(request.Amount, AccountKind.Expense, currency.Value),
            currency.Value);

        var schedule = ScheduleFactory.From(request);
        if (schedule.IsFailure) return schedule.Error!;

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? counterparty.Name
            : request.Description.Trim();

        var now = clock.UtcNow;

        // The rule's two sides are worked out by building one throwaway transaction through the
        // ordinary templates. It is never saved: it exists so that a rule can only ever post
        // something the user could have entered by hand, with no second copy of those rules here.
        var probe = Probe(request, description, account, counterparty, amount, now);
        if (probe.IsFailure) return probe.Error!;

        var debit = probe.Value.Postings.Single(posting => posting.AmountMinor > 0).AccountId;
        var credit = probe.Value.Postings.Single(posting => posting.AmountMinor < 0).AccountId;

        var startDate = request.StartDate == default
            ? await TodayResolver.TodayAsync(settings, clock, cancellationToken)
            : request.StartDate;

        var rule = RecurringRule.Create(
            Guid.CreateVersion7(now), description, request.Payee, debit, credit, amount,
            schedule.Value, startDate, request.EndDate, now);

        if (rule.IsFailure) return rule.Error!;

        rules.Add(rule.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var byId = new Dictionary<Guid, Account>
        {
            [account.Id] = account,
            [counterparty.Id] = counterparty
        };

        var today = await TodayResolver.TodayAsync(settings, clock, cancellationToken);
        return Result<RecurringRuleDto>.Ok(RecurringRuleMapper.ToDto(rule.Value, byId, today));
    }

    private static Result<Transaction> Probe(
        CreateRecurringRuleRequest request, string description,
        Account account, Account counterparty, MoneyValue amount, DateTimeOffset now) =>
        request.Direction switch
        {
            "Income" => LedgerTemplates.Income(
                Guid.Empty, request.StartDate, description, request.Payee,
                receivedInto: account, category: counterparty, amount, now),

            "Transfer" => LedgerTemplates.Transfer(
                Guid.Empty, request.StartDate, description,
                from: account, to: counterparty, amount, now),

            _ => LedgerTemplates.Expense(
                Guid.Empty, request.StartDate, description, request.Payee,
                paidFrom: account, category: counterparty, amount, now)
        };
}
