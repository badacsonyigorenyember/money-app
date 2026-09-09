using Money.Application.Abstractions;

namespace Money.TestSupport;

/// <summary>
/// Rates a test states outright, so nothing here touches the network or the clock. A pair with no
/// entry is unquoted and comes back null, which is exactly what an offline app sees - the case
/// that decides whether an account can be drawn at all.
/// </summary>
public sealed class FixedExchangeRates(params (string From, string To, decimal Rate)[] quotes)
    : IExchangeRates
{
    /// <summary>Nothing is quoted: every conversion fails, as it does with the network off.</summary>
    public static FixedExchangeRates None { get; } = new();

    public Task<decimal?> RateAsync(
        string fromCode, string toCode, CancellationToken cancellationToken = default)
    {
        if (string.Equals(fromCode, toCode, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<decimal?>(1m);

        var quote = quotes.FirstOrDefault(q =>
            string.Equals(q.From, fromCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(q.To, toCode, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(quote.Rate == 0m ? null : (decimal?)quote.Rate);
    }
}
