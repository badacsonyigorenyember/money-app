using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Money.Application.Abstractions;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Api.Infrastructure;

/// <summary>
/// Replays the response of a POST that carried an Idempotency-Key. Costs one table and this
/// filter; buys survival when a phone on a flaky connection retries (spec section 9).
///
/// The key is reserved - inserted and committed - before the handler runs, so a unique-constraint
/// violation on (Key, Endpoint), not a racy "have I seen this key" read, is what stops a second,
/// truly concurrent request under the same key from also creating a transaction. A request that
/// loses that reservation race is told to retry rather than being handed a stale or empty success:
/// there is nothing yet to replay, since the winner has not finished, and "probably fine" is not an
/// acceptable answer for money.
///
/// The request body is hashed and compared on every reservation attempt (win, replay, or race-loss)
/// so a key reused for a materially different request is reported as a conflict instead of
/// silently replaying - or silently colliding with - an unrelated response.
/// </summary>
public sealed class IdempotencyFilter(IIdempotencyStore store, IClock clock) : IEndpointFilter
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

        // The request DTO is always the first bound parameter of the three endpoints this filter
        // wraps (see TransactionEndpoints); model binding has already run by the time a filter
        // sees the invocation context, so this is the deserialized body, not raw bytes.
        var requestBodyHash = HashOf(context.Arguments.Count > 0 ? context.Arguments[0] : null);

        var reservation = await store.TryReserveAsync(key, endpoint, requestBodyHash, clock.UtcNow, http.RequestAborted);

        if (!reservation.Reserved)
        {
            if (!string.Equals(reservation.RequestBodyHash, requestBodyHash, StringComparison.Ordinal))
            {
                return DomainErrorResults.ToProblem(DomainErrors.Idempotency.KeyReusedWithDifferentPayload());
            }

            if (reservation.ResponseJson is null)
            {
                // Someone else holds this key and hasn't finished yet. Replaying a stale or empty
                // success here would be worse than an honest "not done yet" - a 409 tells a caller
                // to back off and retry, rather than being told the operation succeeded when this
                // request never ran it.
                return DomainErrorResults.ToProblem(DomainErrors.Idempotency.RequestInProgress());
            }

            http.Response.Headers["Idempotency-Replayed"] = "true";
            return Results.Content(reservation.ResponseJson, "application/json", statusCode: StatusCodes.Status200OK);
        }

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await store.ReleaseAsync(key, endpoint, http.RequestAborted);
            throw;
        }

        if (result is IValueHttpResult { Value: not null } valued
            && result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
        {
            await store.CompleteAsync(
                key, endpoint, JsonSerializer.Serialize(valued.Value, JsonOptions), http.RequestAborted);
        }
        else
        {
            // The handler ran but did not succeed (a domain-error Problem Details response, most
            // often): release the reservation so a retry with the same key gets a fresh attempt
            // instead of being permanently locked out under a key that never produced a response.
            await store.ReleaseAsync(key, endpoint, http.RequestAborted);
        }

        return result;
    }

    private static string HashOf(object? value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
