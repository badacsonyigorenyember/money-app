using Money.Api.Infrastructure;
using Money.Application.Categories;
using Money.Application.Contracts;

namespace Money.Api.Endpoints;

public static class CategoryEndpoints
{
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder group)
    {
        var categories = group.MapGroup("/categories").WithTags("Categories");

        categories.MapGet("/", async (
            string? kind, bool? includeArchived, GetCategoryTreeHandler handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(
                kind ?? "Expense", includeArchived ?? false, cancellationToken)))
            .WithName("GetCategoryTree");

        categories.MapPost("/", async (
            CreateCategoryRequest request, CreateCategoryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/accounts/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        }).WithName("CreateCategory");

        return group;
    }
}
