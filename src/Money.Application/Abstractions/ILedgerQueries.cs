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

public sealed record TransactionRow(
    Guid Id,
    DateOnly OccurredOn,
    string Description,
    string? Payee,
    bool IsVoided,
    string CurrencyCode,
    long SignedAmountMinor,
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

    Task<IReadOnlyList<AccountBalanceRow>> AllBalancesAsync(CancellationToken cancellationToken = default);
}
