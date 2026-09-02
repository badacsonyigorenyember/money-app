using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Accounts;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository(MoneyDbContext context) : IAccountRepository
{
    public Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Account?> FindByPathAsync(string path, CancellationToken cancellationToken = default) =>
        context.Accounts.FirstOrDefaultAsync(a => a.Path == path, cancellationToken);

    public Task<Account?> FindFirstByRoleAsync(
        AccountRole role, CancellationToken cancellationToken = default) =>
        context.Accounts.Where(a => a.Role == role && !a.IsArchived)
                        .OrderBy(a => a.Path)
                        .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> ListAsync(
        AccountKind? kind, AccountRole? role, bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var query = context.Accounts.AsQueryable();

        if (kind is { } k) query = query.Where(a => a.Kind == k);
        if (role is { } r) query = query.Where(a => a.Role == r);
        if (!includeArchived) query = query.Where(a => !a.IsArchived);

        return await query.OrderBy(a => a.Path).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await context.Accounts.OrderBy(a => a.Path).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> ChildrenOfAsync(
        Guid? parentId, CancellationToken cancellationToken = default) =>
        await context.Accounts.Where(a => a.ParentAccountId == parentId)
                              .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
                              .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> DescendantsOfAsync(
        string pathPrefix, CancellationToken cancellationToken = default) =>
        await context.Accounts.Where(a => a.Path.StartsWith(pathPrefix))
                              .OrderBy(a => a.Path)
                              .ToListAsync(cancellationToken);

    public void Add(Account account) => context.Accounts.Add(account);
}
