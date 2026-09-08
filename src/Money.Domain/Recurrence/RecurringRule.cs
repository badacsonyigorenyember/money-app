using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Recurrence;

/// <summary>
/// A transaction the user wants repeated - a salary, rent, a subscription (spec 5.8). It stores
/// the two sides it will post to rather than a free-form list: every shape the app can enter is
/// two-sided, and the sides are worked out once, at rule creation, by the same
/// <see cref="LedgerTemplates"/> that a one-off entry goes through. A rule can therefore never
/// materialise a transaction a user could not have typed by hand.
/// </summary>
public sealed class RecurringRule
{
    // EF materialisation constructor.
    private RecurringRule()
    {
        Description = null!;
        CurrencyCode = null!;
        Schedule = null!;
    }

    private RecurringRule(
        Guid id, string description, string? payee,
        Guid debitAccountId, Guid creditAccountId, MoneyValue amount,
        Schedule schedule, DateOnly startDate, DateOnly? endDate, DateTimeOffset nowUtc)
    {
        Id = id;
        Description = description;
        Payee = payee;
        DebitAccountId = debitAccountId;
        CreditAccountId = creditAccountId;
        AmountMinor = amount.AmountMinor;
        CurrencyCode = amount.Currency.Code;
        Schedule = schedule;
        StartDate = startDate;
        EndDate = endDate;
        IsActive = true;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Description { get; private set; }
    public string? Payee { get; private set; }

    /// <summary>The side that goes up by a positive amount. Positive = debit (see CLAUDE.md).</summary>
    public Guid DebitAccountId { get; private set; }

    public Guid CreditAccountId { get; private set; }
    public long AmountMinor { get; private set; }
    public string CurrencyCode { get; private set; }
    public Schedule Schedule { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// The last date this rule has been expanded up to. A watermark, not the guard: the unique
    /// index on (SourceKind, SourceId, OccurredOn) is what actually makes double-posting
    /// impossible.
    /// </summary>
    public DateOnly? LastMaterialisedThrough { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<RecurringRule> Create(
        Guid id, string description, string? payee,
        Guid debitAccountId, Guid creditAccountId, MoneyValue amount,
        Schedule schedule, DateOnly startDate, DateOnly? endDate, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(schedule);

        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return DomainErrors.Transaction.DescriptionRequired();

        if (amount.Sign <= 0) return DomainErrors.Transaction.ZeroAmount();

        if (debitAccountId == creditAccountId)
            return DomainErrors.Transaction.DuplicateAccount(description ?? "");

        if (endDate is { } end && end < startDate)
            return DomainErrors.Schedule.EndBeforeStart(startDate, end);

        return Result<RecurringRule>.Ok(new RecurringRule(
            id, trimmed, payee?.Trim(), debitAccountId, creditAccountId,
            amount, schedule, startDate, endDate, nowUtc));
    }

    /// <summary>
    /// The transaction this rule produces for one occurrence date. Built through
    /// <see cref="Transaction.Create"/> like any other, so an archived account or a currency that
    /// no longer matches is caught here rather than written as a broken entry.
    /// </summary>
    public Result<Transaction> Materialise(
        DateOnly occurredOn,
        IReadOnlyDictionary<Guid, Account> accountsById,
        DateTimeOffset nowUtc)
    {
        var currency = Currency.FromCode(CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var amount = MoneyValue.Of(AmountMinor, currency.Value);

        return Transaction.Create(
            Guid.CreateVersion7(nowUtc), occurredOn, Description, Payee,
            TransactionSourceKind.Recurring, sourceId: Id,
            [new PostingDraft(DebitAccountId, amount), new PostingDraft(CreditAccountId, amount.Negate())],
            accountsById, nowUtc);
    }

    /// <summary>The window this rule still owes occurrences for, given today.</summary>
    public (DateOnly From, DateOnly To) PendingWindow(DateOnly today)
    {
        var from = LastMaterialisedThrough is { } through ? through.AddDays(1) : StartDate;
        var to = EndDate is { } end && end < today ? end : today;
        return (from, to);
    }

    public void MarkMaterialisedThrough(DateOnly date, DateTimeOffset nowUtc)
    {
        if (LastMaterialisedThrough is { } through && through >= date) return;

        LastMaterialisedThrough = date;
        UpdatedAtUtc = nowUtc;
    }

    public void SetActive(bool isActive, DateTimeOffset nowUtc)
    {
        if (IsActive == isActive) return;

        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }

    public override string ToString() =>
        $"{Description} ({Schedule.Frequency}, from {StartDate:O}){(IsActive ? "" : " [paused]")}";
}
