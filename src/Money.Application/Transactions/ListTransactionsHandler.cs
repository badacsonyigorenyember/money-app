using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;

namespace Money.Application.Transactions;

public sealed class ListTransactionsHandler(ILedgerQueries queries)
{
    public async Task<TransactionPageDto> HandleAsync(
        TransactionQuery query, CancellationToken cancellationToken = default) =>
        TransactionMapper.ToPageDto(await queries.ListAsync(query, cancellationToken));
}
