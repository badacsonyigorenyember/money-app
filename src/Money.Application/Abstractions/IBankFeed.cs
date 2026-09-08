using Money.Domain.Primitives;

namespace Money.Application.Abstractions;

/// <summary>
/// One booked line as a bank feed states it. <see cref="AmountMinor"/> already uses the ledger's
/// own convention - positive = debit = money into the account, negative = money out - so no sign
/// is flipped between here and <c>LedgerTemplates.Imported</c>.
///
/// <see cref="ExternalRef"/> is the dedup key and must be stable across fetches of the same line.
/// </summary>
public sealed record BankTransaction(
    string ExternalRef,
    DateOnly BookedOn,
    long AmountMinor,
    string CurrencyCode,
    string Description,
    string? Payee);

/// <summary>
/// A read-only feed of booked transactions for one already-linked bank account.
///
/// Booked only, deliberately: a pending entry mutates, can vanish, and is re-issued under a
/// different identifier once it books - which would defeat the dedup key and import the same
/// purchase twice.
/// </summary>
public interface IBankFeed
{
    Task<Result<IReadOnlyList<BankTransaction>>> FetchBookedAsync(
        DateOnly fromInclusive, DateOnly toInclusive, CancellationToken cancellationToken = default);
}
