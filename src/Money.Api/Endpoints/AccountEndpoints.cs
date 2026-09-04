using Money.Api.Infrastructure;
using Money.Application.Accounts;
using Money.Application.Contracts;

namespace Money.Api.Endpoints;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder group)
    {
        var accounts = group.MapGroup("/accounts").WithTags("Accounts");

        accounts.MapGet("/", async (
            string? kind, string? role, bool? includeArchived,
            ListAccountsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(
                kind, role, includeArchived ?? false, cancellationToken)))
            .WithName("ListAccounts");

        accounts.MapPost("/", async (
            CreateAccountRequest request, CreateAccountHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/accounts/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        }).WithName("CreateAccount");

        accounts.MapPatch("/{id:guid}", async (
            Guid id, PatchAccountRequest request, PatchAccountHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("PatchAccount");

        accounts.MapPost("/{id:guid}/archive", async (
            Guid id, ArchiveAccountHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : DomainErrorResults.ToProblem(result);
        }).WithName("ArchiveAccount");

        accounts.MapGet("/{id:guid}/balance", async (
            Guid id, DateOnly? asOf, GetAccountBalanceHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, asOf, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("GetAccountBalance");

        return group;
    }
}
