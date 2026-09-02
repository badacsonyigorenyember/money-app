using Money.Application.Contracts;
using Money.Domain.Accounts;
using Money.Domain.Primitives;

namespace Money.Application.Mapping;

public static class AccountMapper
{
    public static AccountDto ToDto(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountDto(
            account.Id, account.Name, account.Kind.ToString(), account.Role.ToString(),
            account.ParentAccountId, account.Path, account.CurrencyCode, account.IsArchived,
            account.SortOrder, account.ColorHex, account.Icon, account.Notes);
    }

    public static Result<AccountKind> ParseKind(string? kind) =>
        Enum.TryParse<AccountKind>(kind, ignoreCase: true, out var parsed)
            ? Result<AccountKind>.Ok(parsed)
            : DomainErrors.Account.KindRoleMismatch(kind ?? "(none)", "(unknown)");

    public static Result<AccountRole> ParseRole(string? role) =>
        Enum.TryParse<AccountRole>(role, ignoreCase: true, out var parsed)
            ? Result<AccountRole>.Ok(parsed)
            : DomainErrors.Account.KindRoleMismatch("(unknown)", role ?? "(none)");
}
