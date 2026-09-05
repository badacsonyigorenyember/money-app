using Money.Api.Infrastructure;
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Transactions;

namespace Money.Api.Endpoints;

public static class TransactionEndpoints
{
    public static RouteGroupBuilder MapTransactionEndpoints(this RouteGroupBuilder group)
    {
        var transactions = group.MapGroup("/transactions").WithTags("Transactions");

        transactions.MapGet("/", async (
            DateOnly? from, DateOnly? to, Guid? accountId, Guid? categoryId, string? q,
            bool? includeVoided, string? cursor, int? limit,
            ListTransactionsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(
                new TransactionQuery(from, to, accountId, categoryId, q,
                                     includeVoided ?? false, cursor, limit ?? 50),
                cancellationToken)))
            .WithName("ListTransactions");

        transactions.MapPost("/", async (
            CreateTransactionRequest request, CreateTransactionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/transactions/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .WithName("CreateTransaction");

        transactions.MapGet("/{id:guid}", async (
            Guid id, GetTransactionHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("GetTransaction");

        transactions.MapPut("/{id:guid}", async (
            Guid id, CreateTransactionRequest request, ReplaceTransactionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("ReplaceTransaction");

        transactions.MapPost("/{id:guid}/void", async (
            Guid id, VoidTransactionRequest request, VoidTransactionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request.Reason, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : DomainErrorResults.ToProblem(result);
        }).WithName("VoidTransaction");

        transactions.MapPost("/quick-expense", async (
            QuickExpenseRequest request, QuickExpenseHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/transactions/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .WithName("QuickExpense");

        transactions.MapPost("/transfer", async (
            TransferRequest request, TransferHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/transactions/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .WithName("Transfer");

        return group;
    }
}
