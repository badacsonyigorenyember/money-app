using System.Text.Json;
using Money.Application.Abstractions;
using Money.Domain.Time;

namespace Money.Api.Infrastructure;

/// <summary>
/// Replays the response of a POST that carried an Idempotency-Key. Costs one table and this
/// filter; buys survival when a phone on a flaky connection retries (spec section 9).
/// </summary>
public sealed class IdempotencyFilter(IIdempotencyStore store, IUnitOfWork unitOfWork, IClock clock)
    : IEndpointFilter
{
    private const string HeaderName = "Idempotency-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(HeaderName, out var values)) return await next(context);

        var key = values.ToString();
        if (string.IsNullOrWhiteSpace(key)) return await next(context);

        var endpoint = http.Request.Path.Value ?? "";
        var replay = await store.TryGetResponseAsync(key, endpoint, http.RequestAborted);

        if (replay is not null)
        {
            http.Response.Headers["Idempotency-Replayed"] = "true";
            return Results.Content(replay, "application/json", statusCode: StatusCodes.Status200OK);
        }

        var result = await next(context);

        if (result is IValueHttpResult { Value: not null } valued
            && result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
        {
            await store.RecordAsync(
                key, endpoint, JsonSerializer.Serialize(valued.Value, JsonOptions),
                clock.UtcNow, http.RequestAborted);
            await unitOfWork.SaveChangesAsync(http.RequestAborted);
        }

        return result;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
