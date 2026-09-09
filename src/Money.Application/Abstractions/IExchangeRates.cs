namespace Money.Application.Abstractions;

/// <summary>
/// Conversion between currencies, for display only. A rate never reaches the ledger: it draws one
/// chart in one currency and is thrown away, exactly as projected interest is never written as a
/// transaction. Postings keep the currency they were recorded in, and no stored amount is ever
/// re-expressed by anything here.
/// </summary>
public interface IExchangeRates
{
    /// <summary>
    /// How many units of <paramref name="toCode"/> one unit of <paramref name="fromCode"/> buys,
    /// 1 when the two are the same, and null when it cannot be known - no network, or a currency
    /// the source does not quote. A caller handed null shows the original currency rather than
    /// inventing a number.
    /// </summary>
    Task<decimal?> RateAsync(
        string fromCode, string toCode, CancellationToken cancellationToken = default);
}
