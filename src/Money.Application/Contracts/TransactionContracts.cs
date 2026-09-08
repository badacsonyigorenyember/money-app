namespace Money.Application.Contracts;

public sealed record TransactionLineRequest(Guid AccountId, decimal Amount, string? Memo);

public sealed record CreateTransactionRequest(
    DateOnly OccurredOn, string Description, string? Payee,
    IReadOnlyList<TransactionLineRequest> Lines);

public sealed record TransactionLineDto(
    Guid AccountId, string AccountName, string AccountPath, string AccountKind,
    decimal Amount, string CurrencyCode, string? Memo);

public sealed record TransactionDto(
    Guid Id, DateOnly OccurredOn, string Description, string? Payee,
    string SourceKind, bool IsVoided, string? VoidReason,
    IReadOnlyList<TransactionLineDto> Lines);

public sealed record TransactionListItemDto(
    Guid Id, DateOnly OccurredOn, string Description, string? Payee,
    bool IsVoided, decimal Amount, string CurrencyCode,
    string? CategoryName, string? AccountName);

public sealed record TransactionPageDto(IReadOnlyList<TransactionListItemDto> Items, string? NextCursor);

public sealed record QuickEntryRequest(
    decimal Amount, Guid CategoryId, Guid AccountId, DateOnly? OccurredOn,
    string? Description, string? Payee);

public sealed record TransferRequest(
    decimal Amount, Guid FromAccountId, Guid ToAccountId, DateOnly? OccurredOn, string? Description);

public sealed record VoidTransactionRequest(string Reason);
