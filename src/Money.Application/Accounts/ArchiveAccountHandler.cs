using Money.Application.Abstractions;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Accounts;

public sealed class ArchiveAccountHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(id, cancellationToken);
        if (account is null) return Result.Fail(DomainErrors.Account.NotFound(id));

        var archived = account.Archive(clock.UtcNow);
        if (archived.IsFailure) return archived;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
