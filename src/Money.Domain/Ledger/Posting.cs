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

        if (!string.Equals(currency.Code, CurrencyCode, StringComparison.Ordinal))
        {
            var actual = Currency.FromCode(CurrencyCode);
            throw new CurrencyMismatchException(
                actual.IsSuccess ? actual.Value : currency, currency);
        }

        return MoneyValue.Of(AmountMinor, currency);
    }

    /// <summary>Test seam. Production code only ever gets postings from Transaction.Create.</summary>
    internal static Posting CreateForTest(
        Guid id, Guid transactionId, Guid accountId, long amountMinor, string currencyCode, string? memo) =>
        new(id, transactionId, accountId, amountMinor, currencyCode, memo);
}
