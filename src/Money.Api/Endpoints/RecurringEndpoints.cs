using Money.Api.Infrastructure;
using Money.Application.Contracts;
using Money.Application.Recurring;

namespace Money.Api.Endpoints;

public static class RecurringEndpoints
{
    public static RouteGroupBuilder MapRecurringEndpoints(this RouteGroupBuilder group)
    {
        var rules = group.MapGroup("/recurring-rules").WithTags("Recurring");

        rules.MapGet("/", async (
            bool? includePaused, ListRecurringRulesHandler handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(includePaused ?? true, cancellationToken)))
            .WithName("ListRecurringRules");

        rules.MapPost("/", async (
            CreateRecurringRuleRequest request, CreateRecurringRuleHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/recurring-rules/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .WithName("CreateRecurringRule");

        rules.MapPost("/{id:guid}/pause", async (
            Guid id, UpdateRecurringRuleHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.SetActiveAsync(id, false, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : DomainErrorResults.ToProblem(result);
        }).WithName("PauseRecurringRule");

        rules.MapPost("/{id:guid}/resume", async (
            Guid id, UpdateRecurringRuleHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.SetActiveAsync(id, true, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : DomainErrorResults.ToProblem(result);
        }).WithName("ResumeRecurringRule");

        rules.MapDelete("/{id:guid}", async (
            Guid id, UpdateRecurringRuleHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.DeleteAsync(id, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : DomainErrorResults.ToProblem(result);
        }).WithName("DeleteRecurringRule");

        // Manual catch-up. Idempotent by construction, so it is safe to call at any time.
        group.MapPost("/recurring/run", async (
            RecurringMaterialiser materialiser, CancellationToken cancellationToken) =>
            Results.Ok(new { posted = await materialiser.RunAsync(cancellationToken) }))
            .WithTags("Recurring")
            .WithName("RunRecurring");

        return group;
    }
}
