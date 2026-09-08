using Money.Domain.Accounts;

namespace Money.Application.Abstractions;

public sealed record AccountBalanceRow(Guid AccountId, long BalanceMinor);

public sealed record TransactionQuery(
    DateOnly? From,
    DateOnly? To,
    Guid? AccountId,
    Guid? CategoryId,
    string? Text,
    bool IncludeVoided,
    string? Cursor,
    int Limit);

/// <summary>
/// One row's "headline" leg. <paramref name="HeadlineKind"/> is the account kind of whichever
/// posting <paramref name="SignedAmountMinor"/> was taken from - the presentation layer needs it
/// to apply DisplayAmountMapper's sign convention; the raw stored minor units alone are not
/// display-safe for every account kind (Income, Liability and Equity legs are stored negative for
/// an increase and must be negated to read as positive to a user).
/// </summary>
public sealed record TransactionRow(
    Guid Id,
    DateOnly OccurredOn,
    string Description,
    string? Payee,
    bool IsVoided,
    string CurrencyCode,
    long SignedAmountMinor,
    AccountKind HeadlineKind,
    string? CategoryName,
    string? AccountName);

public sealed record TransactionPage(IReadOnlyList<TransactionRow> Rows, string? NextCursor);

public interface ILedgerQueries
{
    Task<long> BalanceOfAsync(
        Guid accountId, DateOnly? asOfInclusive, CancellationToken cancellationToken = default);

    /// <summary>Sums the subtree rooted at <paramref name="path"/>, inclusive of the root itself.</summary>
    Task<long> SubtreeBalanceAsync(
        string path, DateOnly? fromInclusive, DateOnly? toExclusive,
        CancellationToken cancellationToken = default);

    Task<TransactionPage> ListAsync(TransactionQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many ledger entries name the subtree rooted at <paramref name="path"/>, inclusive of the
    /// root. Removed transactions count: their entries are still history a reader can open, so an
    /// account named by one is not free to be erased. Zero is what makes a delete safe.
    /// </summary>
    Task<int> SubtreeEntryCountAsync(string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountBalanceRow>> AllBalancesAsync(CancellationToken cancellationToken = default);
}
