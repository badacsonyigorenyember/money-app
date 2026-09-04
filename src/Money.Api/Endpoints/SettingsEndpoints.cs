using Money.Api.Infrastructure;
using Money.Application.Contracts;
using Money.Application.FirstRun;
using Money.Application.Settings;

namespace Money.Api.Endpoints;

public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder group)
    {
        var settings = group.MapGroup("/settings").WithTags("Settings");

        settings.MapGet("/", async (GetSettingsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("GetSettings");

        settings.MapPut("/", async (
            UpdateSettingsRequest request, UpdateSettingsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("UpdateSettings");

        settings.MapPost("/first-run", async (
            FirstRunRequest request, CompleteFirstRunSetupHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("CompleteFirstRun");

        return group;
    }
}
