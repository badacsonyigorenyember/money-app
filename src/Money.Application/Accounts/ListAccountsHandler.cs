using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;

namespace Money.Application.Accounts;

public sealed class ListAccountsHandler(IAccountRepository accounts)
{
    public async Task<IReadOnlyList<AccountDto>> HandleAsync(
        string? kind, string? role, bool includeArchived, CancellationToken cancellationToken = default)
    {
        AccountKind? parsedKind = Enum.TryParse<AccountKind>(kind, true, out var k) ? k : null;
        AccountRole? parsedRole = Enum.TryParse<AccountRole>(role, true, out var r) ? r : null;

        var found = await accounts.ListAsync(parsedKind, parsedRole, includeArchived, cancellationToken);
        return found.Select(AccountMapper.ToDto).ToArray();
    }
}
