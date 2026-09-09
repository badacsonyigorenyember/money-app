using Money.Application.Abstractions;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Accounts;

/// <summary>
/// Takes an archived account or category back out of the archive. The mirror of
/// <see cref="ArchiveAccountHandler"/>, and only that: a subtree is archived leaf-first, so it
/// comes back parent-first, one row at a time. Nothing under the restored account is touched.
/// </summary>
public sealed class RestoreAccountHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(id, cancellationToken);
        if (account is null || account.IsDeleted) return Result.Fail(DomainErrors.Account.NotFound(id));

        // An active child under an archived parent is a tree Create would never allow to exist.
        if (account.ParentAccountId is { } parentId)
        {
            var parent = await accounts.FindAsync(parentId, cancellationToken);
            if (parent is { IsArchived: true })
                return Result.Fail(DomainErrors.Account.ParentArchived(parent.Name));
        }

        var restored = account.Restore(clock.UtcNow);
        if (restored.IsFailure) return restored;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
