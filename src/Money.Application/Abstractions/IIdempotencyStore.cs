namespace Money.Application.Abstractions;

public interface IIdempotencyStore
{
    Task<string?> TryGetResponseAsync(string key, string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// The timestamp is passed in rather than read here: no component below the composition root
    /// may read ambient time.
    /// </summary>
    Task RecordAsync(
        string key, string endpoint, string responseJson, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);
}
