using Money.Domain.Accounts;
using Money.Domain.Money;
using Money.Domain.Periods;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Ledger;

/// <summary>
/// The reference implementation of every balance question. Balances are never stored (spec 5.3);
/// the SQL in Money.Infrastructure must agree with this class, and is tested against it.
/// </summary>
public static class BalanceCalculator
{
    public static bool IsInSubtree(Account candidate, Account root)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(root);

        return string.Equals(candidate.Path, root.Path, StringComparison.Ordinal)
               || candidate.Path.StartsWith(root.ChildPathPrefix, StringComparison.Ordinal);
    }

    public static MoneyValue BalanceOf(
        Guid accountId, Currency currency,
        IEnumerable<Transaction> transactions, DateOnly? asOfInclusive = null)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(transactions);

        var total = 0L;

        foreach (var transaction in transactions)
        {
            if (transaction.IsVoided) continue;
            if (asOfInclusive is { } asOf && transaction.OccurredOn > asOf) continue;

            foreach (var posting in transaction.Postings)
            {
                if (posting.AccountId != accountId) continue;
                if (!string.Equals(posting.CurrencyCode, currency.Code, StringComparison.Ordinal)) continue;
                total = checked(total + posting.AmountMinor);
            }
        }

        return MoneyValue.Of(total, currency);
    }

    public static MoneyValue SubtreeBalance(
        Account root, Currency currency,
        IReadOnlyDictionary<Guid, Account> accountsById,
        IEnumerable<Transaction> transactions,
        DateRange? window = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(accountsById);
        ArgumentNullException.ThrowIfNull(transactions);

        var total = 0L;

        foreach (var transaction in transactions)
        {
            if (transaction.IsVoided) continue;
            if (window is { } range && !range.Contains(transaction.OccurredOn)) continue;

            foreach (var posting in transaction.Postings)
            {
                if (!string.Equals(posting.CurrencyCode, currency.Code, StringComparison.Ordinal)) continue;
                if (!accountsById.TryGetValue(posting.AccountId, out var account)) continue;
                if (!IsInSubtree(account, root)) continue;
                total = checked(total + posting.AmountMinor);
            }
        }

        return MoneyValue.Of(total, currency);
    }

    /// <summary>
    /// Total spending in a window: postings to Kind=Expense accounts only. Transfers, pocket
    /// funding and investment purchases touch no expense account, so they are structurally
    /// excluded rather than filtered out by a rule someone could forget (I12).
    /// </summary>
    public static MoneyValue TotalSpending(
        Currency currency,
        IReadOnlyDictionary<Guid, Account> accountsById,
        IEnumerable<Transaction> transactions,
        DateRange window)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(accountsById);
        ArgumentNullException.ThrowIfNull(transactions);

        var total = 0L;

        foreach (var transaction in transactions)
        {
            if (transaction.IsVoided) continue;
            if (!window.Contains(transaction.OccurredOn)) continue;

            foreach (var posting in transaction.Postings)
            {
                if (!string.Equals(posting.CurrencyCode, currency.Code, StringComparison.Ordinal)) continue;
                if (!accountsById.TryGetValue(posting.AccountId, out var account)) continue;
                if (account.Kind != AccountKind.Expense) continue;
                total = checked(total + posting.AmountMinor);
            }
        }

        return MoneyValue.Of(total, currency);
    }
}
