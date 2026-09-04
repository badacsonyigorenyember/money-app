using Money.Application.Abstractions;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Transactions;

public sealed class VoidTransactionHandler(
    ITransactionRepository transactions, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(
        Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var transaction = await transactions.FindAsync(id, cancellationToken);
        if (transaction is null) return Result.Fail(DomainErrors.Transaction.NotFound(id));

        var voided = transaction.Void(reason, clock.UtcNow);
        if (voided.IsFailure) return voided;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
