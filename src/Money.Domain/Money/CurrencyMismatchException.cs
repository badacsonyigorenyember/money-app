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
        Right = right;
    }

    public Currency Left { get; }

    public Currency Right { get; }
}
