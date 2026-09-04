using Microsoft.AspNetCore.Http;

namespace Money.Api.Endpoints;

/// <summary>
/// Placeholder so the composition root (Task 26) compiles and routes ahead of Task 28, which
/// replaces this with the real transaction routes (spec section 9). The one mapped route exists
/// so the OpenAPI document already lists "/api/v1/transactions" (task-26-brief.md, Step 8).
/// </summary>
public static class TransactionEndpoints
{
    public static RouteGroupBuilder MapTransactionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/transactions", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        return group;
    }
}
