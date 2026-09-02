using Money.Domain.Accounts;

namespace Money.Application.Abstractions;

public interface IAccountRepository
{
    Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Account?> FindByPathAsync(string path, CancellationToken cancellationToken = default);

    Task<Account?> FindFirstByRoleAsync(AccountRole role, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> ListAsync(
        AccountKind? kind, AccountRole? role, bool includeArchived,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> ChildrenOfAsync(Guid? parentId, CancellationToken cancellationToken = default);

    /// <summary>Everything strictly below <paramref name="pathPrefix"/> (which must end in '/').</summary>
    Task<IReadOnlyList<Account>> DescendantsOfAsync(string pathPrefix, CancellationToken cancellationToken = default);

    void Add(Account account);
}
