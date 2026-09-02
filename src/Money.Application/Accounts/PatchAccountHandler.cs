using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Accounts;

public sealed class PatchAccountHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<AccountDto>> HandleAsync(
        Guid id, PatchAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await accounts.FindAsync(id, cancellationToken);
        if (account is null) return DomainErrors.Account.NotFound(id);

        var now = clock.UtcNow;

        if (request.Name is { } newName && !string.Equals(newName, account.Name, StringComparison.Ordinal))
        {
            var siblings = await accounts.ChildrenOfAsync(account.ParentAccountId, cancellationToken);
            var descendants = await accounts.DescendantsOfAsync(account.ChildPathPrefix, cancellationToken);

            var renamed = AccountTree.Rename(account, newName, siblings, descendants, now);
            if (renamed.IsFailure) return renamed.Error!;
        }

        if (request.ClearParent == true || request.ParentAccountId is not null)
        {
            Account? newParent = null;
            if (request.ClearParent != true && request.ParentAccountId is { } parentId)
            {
                newParent = await accounts.FindAsync(parentId, cancellationToken);
                if (newParent is null) return DomainErrors.Account.NotFound(parentId);
            }

            var newSiblings = await accounts.ChildrenOfAsync(newParent?.Id, cancellationToken);
            var descendants = await accounts.DescendantsOfAsync(account.ChildPathPrefix, cancellationToken);

            var moved = AccountTree.Move(account, newParent, newSiblings, descendants, now);
            if (moved.IsFailure) return moved.Error!;
        }

        account.UpdatePresentation(
            request.SortOrder ?? account.SortOrder,
            request.ColorHex ?? account.ColorHex,
            request.Icon ?? account.Icon,
            request.Notes ?? account.Notes,
            account.OpenedOn, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AccountDto>.Ok(AccountMapper.ToDto(account));
    }
}
