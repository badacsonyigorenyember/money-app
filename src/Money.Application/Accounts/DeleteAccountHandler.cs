using Money.Application.Abstractions;
using Money.Domain.Accounts;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Accounts;

/// <summary>
/// Deletes an archived account or category, and everything filed underneath it, for good.
///
/// There are two outcomes, and which one applies depends only on whether the ledger still names
/// the subtree. Nothing refers to it: the rows are erased, and the app is left exactly as if the
/// account had never existed. Something refers to it: the rows stay, flagged deleted, out of every
/// list, picker, archive and name-collision check - because history the user recorded has to keep
/// saying what an entry was for, and a row is the only thing that can still say it.
///
/// Either way the transactions themselves are untouched. Deleting a category has never been a way
/// to delete spending.
/// </summary>
public sealed class DeleteAccountHandler(
    IAccountRepository accounts, ILedgerQueries queries, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(id, cancellationToken);
        if (account is null || account.IsDeleted) return Result.Fail(DomainErrors.Account.NotFound(id));

        if (!account.IsArchived)
            return Result.Fail(DomainErrors.Account.DeleteNeedsArchiveFirst(account.Name, account.IsCategory));

        var subtree = new List<Account> { account };
        subtree.AddRange(
            (await accounts.DescendantsOfAsync(account.ChildPathPrefix, cancellationToken))
            .Where(descendant => !descendant.IsDeleted));

        // One decision for the whole subtree, not one per account: a hard-deleted parent with a
        // kept child would leave that child pointing at a row that no longer exists.
        var entries = await queries.SubtreeEntryCountAsync(account.Path, cancellationToken);
        var now = clock.UtcNow;

        foreach (var member in subtree)
        {
            if (entries == 0)
            {
                accounts.Remove(member);
                continue;
            }

            // A descendant may still be active - archiving the root does not archive what is under
            // it - and Delete insists on archived. Cascading is the user's explicit instruction, so
            // take that step for them rather than refusing.
            if (!member.IsArchived) member.Archive(now);

            var deleted = member.Delete(now);
            if (deleted.IsFailure) return deleted;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
