using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Accounts;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository(MoneyDbContext context) : IAccountRepository
{
    // A deleted account is not part of the tree any more. It is still findable by id, because
    // ledger history refers to it by id and has to be able to name it, and it is still a
    // descendant for path bookkeeping - but it is in no list, no picker and no name-collision
    // check, which is what lets a deleted name be used again.
    private IQueryable<Account> Living => context.Accounts.Where(a => !a.IsDeleted);

    public Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Account?> FindByPathAsync(string path, CancellationToken cancellationToken = default) =>
        Living.FirstOrDefaultAsync(a => a.Path == path, cancellationToken);

    public Task<Account?> FindFirstByRoleAsync(
        AccountRole role, CancellationToken cancellationToken = default) =>
        Living.Where(a => a.Role == role && !a.IsArchived)
              .OrderBy(a => a.Path)
              .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> ListAsync(
        AccountKind? kind, AccountRole? role, bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var query = Living;

        if (kind is { } k) query = query.Where(a => a.Kind == k);
        if (role is { } r) query = query.Where(a => a.Role == r);
        if (!includeArchived) query = query.Where(a => !a.IsArchived);

        return await query.OrderBy(a => a.Path).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await context.Accounts.OrderBy(a => a.Path).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> ChildrenOfAsync(
        Guid? parentId, CancellationToken cancellationToken = default) =>
        await Living.Where(a => a.ParentAccountId == parentId)
                    .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
                    .ToListAsync(cancellationToken);

    // Deleted rows stay in this one. A rename higher up the tree rewrites every descendant path in
    // the same call, and skipping the deleted ones would leave their paths pointing at a parent
    // path that no longer exists - which the integrity check would then report as broken.
    public async Task<IReadOnlyList<Account>> DescendantsOfAsync(
        string pathPrefix, CancellationToken cancellationToken = default) =>
        await context.Accounts.Where(a => a.Path.StartsWith(pathPrefix))
                              .OrderBy(a => a.Path)
                              .ToListAsync(cancellationToken);

    public void Add(Account account) => context.Accounts.Add(account);

    public void Remove(Account account) => context.Accounts.Remove(account);
}
