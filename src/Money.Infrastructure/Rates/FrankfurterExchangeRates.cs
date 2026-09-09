using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Money.Application.Abstractions;
using Money.Domain.Time;

namespace Money.Infrastructure.Rates;

/// <summary>
/// Rates from Frankfurter (https://github.com/lineofflight/frankfurter), which republishes the
/// European Central Bank's daily reference rates and needs no key or account.
///
/// This app has to work with the network switched off, so a failed fetch is not an error: the
/// rate comes back null and the caller keeps showing the account's own currency. One table per
/// base currency is cached, because the ECB publishes once a working day and asking more often
/// buys nothing; a fetch that failed is remembered too, for much less time, so an offline window
/// costs one request rather than one per account per page load.
/// </summary>
public sealed partial class FrankfurterExchangeRates(
    HttpClient http, IClock clock, ILogger<FrankfurterExchangeRates> logger) : IExchangeRates
{
    public const string ClientName = "frankfurter";

    private static readonly TimeSpan Freshness = TimeSpan.FromHours(12);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyDictionary<string, decimal> Unavailable =
        new Dictionary<string, decimal>(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ConcurrentDictionary<string, CachedTable> _cache = new(StringComparer.Ordinal);

    public async Task<decimal?> RateAsync(
        string fromCode, string toCode, CancellationToken cancellationToken = default)
    {
        var from = Normalise(fromCode);
        var to = Normalise(toCode);

        if (from is null || to is null) return null;
        if (string.Equals(from, to, StringComparison.Ordinal)) return 1m;

        var table = await TableAsync(from, cancellationToken);
        return table.TryGetValue(to, out var rate) ? rate : null;
    }

    private async Task<IReadOnlyDictionary<string, decimal>> TableAsync(
        string baseCode, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        if (_cache.TryGetValue(baseCode, out var cached) && now - cached.FetchedAt < cached.GoodFor)
            return cached.Rates;

        IReadOnlyDictionary<string, decimal> fetched;

        try
        {
            fetched = await FetchAsync(baseCode, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser dropped the request - a reload, or htmx swapping one panel for another.
            // That says nothing about whether the source is reachable, so it must not be written
            // down as a failure: doing so once took an account off the chart for every reload in
            // the next five minutes, which reads as "this account can never be drawn".
            return cached?.Rates ?? Unavailable;
        }

        // A fetch that failed must not erase a table that once worked. Yesterday's rate draws a
        // better chart than no chart, so only the retry timer moves and the last good rates stay.
        var kept = fetched.Count == 0 && cached is { Rates.Count: > 0 } ? cached.Rates : fetched;

        _cache[baseCode] = new CachedTable(
            now, kept, fetched.Count == 0 ? RetryAfterFailure : Freshness);

        return kept;
    }

    private async Task<IReadOnlyDictionary<string, decimal>> FetchAsync(
        string baseCode, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.GetFromJsonAsync<LatestRates>(
                "v1/latest?base=" + baseCode, Json, cancellationToken);

            return response?.Rates is { Count: > 0 } rates
                ? new Dictionary<string, decimal>(rates, StringComparer.Ordinal)
                : Unavailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Offline, rate-limited, or a shape this code does not recognise. None of those is
            // worth failing a page over - the chart falls back to per-account currencies. It is
            // logged, because from the outside it looks like an account that cannot be ticked.
            LogFetchFailed(logger, baseCode, ex);
            return Unavailable;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout, which is a slow or unreachable source and so a real
            // failure. A cancellation that came from the caller is left to propagate.
            LogFetchFailed(logger, baseCode, ex);
            return Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No exchange rates for base {BaseCode}; accounts in other currencies stay off the chart")]
    private static partial void LogFetchFailed(ILogger logger, string baseCode, Exception exception);

    private static string? Normalise(string? code)
    {
        var trimmed = code?.Trim();
        return trimmed is { Length: 3 } && trimmed.All(char.IsAsciiLetter)
            ? trimmed.ToUpperInvariant()
            : null;
    }

    private sealed record CachedTable(
        DateTimeOffset FetchedAt, IReadOnlyDictionary<string, decimal> Rates, TimeSpan GoodFor);

    /// <summary>Frankfurter's shape: <c>{"amount":1.0,"base":"EUR","rates":{"HUF":363.95}}</c>.</summary>
    private sealed record LatestRates(
        [property: JsonPropertyName("base")] string? Base,
        [property: JsonPropertyName("rates")] Dictionary<string, decimal>? Rates);
}
