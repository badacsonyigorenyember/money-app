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
