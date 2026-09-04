using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Transactions;

public sealed class ReplaceTransactionHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        Guid id, CreateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var transaction = await transactions.FindAsync(id, cancellationToken);
        if (transaction is null) return DomainErrors.Transaction.NotFound(id);

        var resolved = await TransactionLineResolver.ResolveAsync(
            accounts, request.Lines, cancellationToken);
        if (resolved.IsFailure) return resolved.Error!;

        var replaced = transaction.Replace(
            request.OccurredOn, request.Description ?? "", request.Payee,
            resolved.Value.Drafts, resolved.Value.AccountsById, clock.UtcNow);

        if (replaced.IsFailure) return replaced.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<TransactionDto>.Ok(
            TransactionMapper.ToDto(transaction, resolved.Value.AccountsById));
    }
}
