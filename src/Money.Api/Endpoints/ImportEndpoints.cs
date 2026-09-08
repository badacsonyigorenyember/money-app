using Money.Api.Infrastructure;
using Money.Application.Contracts;
using Money.Application.Import;

namespace Money.Api.Endpoints;

public static class ImportEndpoints
{
    public static RouteGroupBuilder MapImportEndpoints(this RouteGroupBuilder group)
    {
        var import = group.MapGroup("/import").WithTags("Import");

        // Safe to call as often as you like: the external reference makes a re-run a no-op. The
        // bank's own rate limit is the real constraint, not this endpoint.
        import.MapPost("/bank", async (
            ImportBankTransactionsRequest request,
            ImportBankTransactionsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("ImportBankTransactions");

        return group;
    }
}
