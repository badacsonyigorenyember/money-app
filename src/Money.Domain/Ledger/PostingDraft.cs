using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Ledger;

/// <summary>One side of a transaction, as supplied by a caller. Positive is a debit.</summary>
public sealed record PostingDraft(Guid AccountId, MoneyValue Amount, string? Memo = null);
