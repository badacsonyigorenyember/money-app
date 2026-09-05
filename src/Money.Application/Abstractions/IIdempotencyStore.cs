namespace Money.Application.Abstractions;

/// <summary>
/// The outcome of attempting to reserve an idempotency key before its handler runs.
///
/// <c>Reserved</c> is true when this call is the one that created the reservation: the caller now
/// owns the key and must eventually call <see cref="IIdempotencyStore.CompleteAsync"/> (handler
/// succeeded) or <see cref="IIdempotencyStore.ReleaseAsync"/> (handler failed).
///
/// <c>Reserved</c> is false when a reservation for (key, endpoint) already existed.
/// <c>RequestBodyHash</c> is the hash recorded by whoever holds it, so the caller can tell a
/// genuine retry (same hash) from a reused key on a different request (different hash).
/// <c>ResponseJson</c> is null while that reservation's handler is still running - there is
/// nothing to replay yet - and set once it has completed.
/// </summary>
public sealed record IdempotencyReservation(bool Reserved, string? RequestBodyHash, string? ResponseJson);

public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically reserves (key, endpoint) so at most one caller ever proceeds to run the
    /// handler for a given key: the reservation is written and committed here, before the
    /// caller does anything else, so a unique-constraint violation - not a racy read-then-write
    /// check - is what stops a concurrent duplicate.
    /// </summary>
    Task<IdempotencyReservation> TryReserveAsync(
        string key, string endpoint, string requestBodyHash, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Records the response for a reservation this caller made, so retries can replay it.</summary>
    Task CompleteAsync(
        string key, string endpoint, string responseJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a reservation this caller made whose handler did not succeed, so a later retry
    /// with the same key can attempt the operation again instead of being locked out forever.
    /// </summary>
    Task ReleaseAsync(string key, string endpoint, CancellationToken cancellationToken = default);
}
