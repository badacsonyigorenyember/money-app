namespace Money.Application.Contracts;

/// <summary>
/// <paramref name="WindowDays"/> defaults to 35 rather than "this month": card purchases book
/// days after they happen, so a window that resets at a month boundary loses every line that
/// straddles it. Overlap is free - the external reference deduplicates it.
/// </summary>
public sealed record ImportBankTransactionsRequest(Guid AccountId, int? WindowDays = null);

public sealed record ImportResultDto(
    DateOnly From,
    DateOnly To,
    int Fetched,
    int Imported,
    int AlreadyPresent,
    int SkippedForeignCurrency);
