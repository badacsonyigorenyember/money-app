namespace Money.Infrastructure.Persistence;

/// <summary>
/// Replays the response of a POST that carried an Idempotency-Key. One table and one filter is
/// the whole cost of surviving a phone retrying on a flaky connection (spec section 9).
/// </summary>
public sealed class IdempotencyRecord
{
    public string Key { get; set; } = null!;
    public string Endpoint { get; set; } = null!;
    public string ResponseJson { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
