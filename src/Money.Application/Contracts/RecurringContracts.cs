namespace Money.Application.Contracts;

/// <summary>
/// One repeating entry, as the screen states it. Enums travel as strings here, the way every
/// other contract in this app states a kind or a role: <c>Direction</c> is Expense, Income or
/// Transfer, <c>Frequency</c> is Daily, Weekly, Monthly, Yearly or Custom, and <c>DayOfWeek</c>
/// is a day name. Each frequency reads only the fields it needs.
/// </summary>
public sealed record CreateRecurringRuleRequest(
    string Direction,
    decimal Amount,
    Guid AccountId,
    Guid CounterpartyId,
    string? Description,
    string? Payee,
    string Frequency,
    int Interval,
    string? DayOfWeek,
    int? WeekOfMonth,
    int? DayOfMonth,
    int? Month,
    int CustomYears,
    int CustomMonths,
    int CustomDays,
    DateOnly StartDate,
    DateOnly? EndDate);

public sealed record RecurringRuleDto(
    Guid Id, string Description, string? Payee, decimal Amount, string CurrencyCode,
    string DebitAccountName, string CreditAccountName,
    string ScheduleSummary, DateOnly StartDate, DateOnly? EndDate,
    bool IsActive, DateOnly? NextOccurrence);
