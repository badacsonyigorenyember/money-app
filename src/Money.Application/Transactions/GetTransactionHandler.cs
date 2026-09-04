using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Primitives;

namespace Money.Application.Transactions;

public sealed class GetTransactionHandler(
    ITransactionRepository transactions, IAccountRepository accounts)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await transactions.FindAsync(id, cancellationToken);
        if (transaction is null) return DomainErrors.Transaction.NotFound(id);

        var accountsById = new Dictionary<Guid, Account>();
        foreach (var posting in transaction.Postings)
        {
            if (accountsById.ContainsKey(posting.AccountId)) continue;

            var account = await accounts.FindAsync(posting.AccountId, cancellationToken);
            if (account is null) return DomainErrors.Transaction.AccountUnknown(posting.AccountId);
            accountsById[posting.AccountId] = account;
        }

        return Result<TransactionDto>.Ok(TransactionMapper.ToDto(transaction, accountsById));
    }
}
