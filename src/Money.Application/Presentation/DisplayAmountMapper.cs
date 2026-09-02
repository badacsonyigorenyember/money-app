using Money.Domain.Accounts;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Presentation;

/// <summary>
/// The single place the ledger's sign convention is translated for humans.
///
/// Internally: positive = debit. Asset and Expense grow positive; Income, Liability and Equity
/// grow negative. A user asked "how much did I earn?" expects a positive number, so those three
/// kinds are negated here - and nowhere else. If you find another sign flip in the codebase,
/// it is a bug.
/// </summary>
public static class DisplayAmountMapper
{
    public static bool IsNegatedForDisplay(AccountKind kind) =>
        kind is AccountKind.Income or AccountKind.Liability or AccountKind.Equity;

    public static decimal ToDisplay(long amountMinor, AccountKind kind, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var oriented = IsNegatedForDisplay(kind) ? -amountMinor : amountMinor;
        return oriented / currency.MinorUnitScale;
    }

    public static long ToStored(decimal displayed, AccountKind kind, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var minor = MoneyValue.RoundToMinor(displayed * currency.MinorUnitScale);
        return IsNegatedForDisplay(kind) ? -minor : minor;
    }
}
