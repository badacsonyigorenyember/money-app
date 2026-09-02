namespace Money.Domain.Money;

/// <summary>
/// Thrown when arithmetic mixes currencies. A programmer error, not a user error,
/// so it throws rather than returning a Result.
/// </summary>
public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot combine {left.Code} and {right.Code} amounts.")
    {
        Left = left;
        LeftCode = left.Code;
        Right = right;
    }

    /// <summary>
    /// For a mismatch where the left-hand side is a raw, persisted currency code that does not
    /// resolve to a known <see cref="Money.Domain.Money.Currency"/> (corrupted or legacy data) -
    /// see <c>Posting.AmountIn</c>. Naming the raw code, instead of substituting the right-hand
    /// currency for it, avoids a message that claims a currency mismatches itself.
    /// </summary>
    public CurrencyMismatchException(string leftCode, Currency right)
        : base($"Cannot combine {leftCode} and {right.Code} amounts.")
    {
        ArgumentNullException.ThrowIfNull(leftCode);
        ArgumentNullException.ThrowIfNull(right);
        Left = null;
        LeftCode = leftCode;
        Right = right;
    }

    /// <summary>Null when the left-hand side was an unresolvable raw code; see <see cref="LeftCode"/>.</summary>
    public Currency? Left { get; }

    /// <summary>The left-hand side's currency code, always populated even when <see cref="Left"/> is null.</summary>
    public string LeftCode { get; }

    public Currency Right { get; }
}
