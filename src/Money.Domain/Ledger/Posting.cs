using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Ledger;

/// <summary>
/// One side of a persisted transaction. Positive is a debit: Asset and Expense accounts increase
/// with a positive amount, Income, Liability and Equity accounts with a negative one.
/// </summary>
public sealed class Posting
{
    // EF Core materialisation constructor.
    private Posting() => CurrencyCode = null!;

    internal Posting(
        Guid id, Guid transactionId, Guid accountId, long amountMinor, string currencyCode, string? memo)
    {
        Id = id;
        TransactionId = transactionId;
        AccountId = accountId;
        AmountMinor = amountMinor;
        CurrencyCode = currencyCode;
        Memo = memo;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public long AmountMinor { get; private set; }
    public string CurrencyCode { get; private set; }
    public string? Memo { get; private set; }

    public MoneyValue AmountIn(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var stored = Currency.FromCode(CurrencyCode);
        if (stored.IsSuccess)
        {
            // The stored code resolves to a known Currency: judge "same currency" exactly as
            // Money.RequireSameCurrency does - full record equality (Code and MinorUnitExponent),
            // not Code alone.
            if (!stored.Value.Equals(currency))
                throw new CurrencyMismatchException(stored.Value, currency);
        }
        else if (!string.Equals(CurrencyCode, currency.Code, StringComparison.Ordinal))
        {
            // The stored code does not resolve (corrupted or legacy data). We cannot recover its
            // original minor-unit exponent, so an ordinal code comparison is the best check
            // available - a code match is accepted rather than rejected outright, since an
            // unresolvable stored code is not by itself an error. Name the raw stored code rather
            // than substituting the requested currency for it.
            throw new CurrencyMismatchException(CurrencyCode, currency);
        }

        return MoneyValue.Of(AmountMinor, currency);
    }

    /// <summary>Test seam. Production code only ever gets postings from Transaction.Create.</summary>
    internal static Posting CreateForTest(
        Guid id, Guid transactionId, Guid accountId, long amountMinor, string currencyCode, string? memo) =>
        new(id, transactionId, accountId, amountMinor, currencyCode, memo);
}
