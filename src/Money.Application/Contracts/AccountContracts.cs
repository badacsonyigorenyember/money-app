namespace Money.Application.Contracts;

public sealed record AccountDto(
    Guid Id, string Name, string Kind, string Role, Guid? ParentAccountId,
    string Path, string CurrencyCode, bool IsArchived,
    int SortOrder, string? ColorHex, string? Icon, string? Notes);

public sealed record AccountBalanceDto(
    Guid AccountId, string Name, decimal Balance, string CurrencyCode, DateOnly? AsOf);

public sealed record CreateAccountRequest(
    string Name, string Kind, string Role, Guid? ParentAccountId,
    string? CurrencyCode, decimal? OpeningBalance, DateOnly? OpenedOn);

public sealed record PatchAccountRequest(
    string? Name, Guid? ParentAccountId, bool? ClearParent,
    int? SortOrder, string? ColorHex, string? Icon, string? Notes);

/// <summary>Where an account stood at the end of one day of the month.</summary>
public sealed record DayBalanceDto(DateOnly Day, decimal Balance);

/// <summary>
/// One account over one month, already oriented for a reader: <paramref name="MoneyIn"/> and
/// <paramref name="MoneyOut"/> are both positive, and <paramref name="Kept"/> is what is left of
/// the one after the other - negative in a month that spent more than it came in.
///
/// <paramref name="OpeningBalance"/> is what the account carried in from the month before, which
/// is where its line starts and where its baseline sits. <paramref name="Days"/> holds the closing
/// balance of each day that has actually happened, so a month in progress draws a partial line
/// rather than a flat one running into the future.
///
/// Every amount here is in the account's own <paramref name="CurrencyCode"/>.
/// <paramref name="RateToBase"/> is what one of those units is worth in the ledger's base
/// currency - 1 when they are the same currency, and null when no rate could be had, which is the
/// chart's signal that this account cannot honestly share its axis. It is a display rate and
/// nothing else: no stored amount is ever re-expressed by it.
/// </summary>
public sealed record AccountMonthDto(
    Guid AccountId, string Name, string CurrencyCode,
    decimal OpeningBalance, decimal ClosingBalance,
    decimal MoneyIn, decimal MoneyOut, decimal Kept,
    IReadOnlyList<DayBalanceDto> Days,
    decimal? RateToBase);

/// <summary>
/// The home page's chart and the entry list both read one month, named by
/// <paramref name="PeriodKey"/>. <paramref name="NextKey"/> is null in the month in progress:
/// there is nothing useful to page forward into.
/// </summary>
public sealed record MonthOverviewDto(
    string PeriodKey, string Label, DateOnly Start, DateOnly EndInclusive, int DayCount,
    string BaseCurrencyCode, string PreviousKey, string? NextKey,
    IReadOnlyList<AccountMonthDto> Accounts);
