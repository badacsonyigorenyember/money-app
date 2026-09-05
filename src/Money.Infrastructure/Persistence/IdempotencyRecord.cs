namespace Money.Infrastructure.Persistence;

/// <summary>
/// Replays the response of a POST that carried an Idempotency-Key. One table and one filter is
/// the whole cost of surviving a phone retrying on a flaky connection (spec section 9).
///
/// A row exists from the moment a key is reserved, before its handler runs: <see cref="ResponseJson"/>
/// is null while the handler is in flight and is filled in once it completes. That is what lets the
/// unique key (Key, Endpoint) - not a racy read-then-write check - be the thing that stops a second,
/// truly concurrent request under the same key from also creating a transaction.
/// </summary>
public sealed class IdempotencyRecord
{
    public string Key { get; set; } = null!;
    public string Endpoint { get; set; } = null!;
    public string RequestBodyHash { get; set; } = null!;
    public string? ResponseJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
