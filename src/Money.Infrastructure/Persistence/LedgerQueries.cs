using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Application.Presentation;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// Read-model SQL. Every query here is checked against BalanceCalculator in the domain, which is
/// the specification; this class is only an optimisation over it.
/// </summary>
public sealed class LedgerQueries(MoneyDbContext context) : ILedgerQueries
{
    public async Task<long> BalanceOfAsync(
        Guid accountId, DateOnly? asOfInclusive, CancellationToken cancellationToken = default)
    {
        var query = context.Postings
            .Join(context.Transactions, p => p.TransactionId, t => t.Id, (p, t) => new { p, t })
            .Where(x => x.p.AccountId == accountId && !x.t.IsVoided);

        if (asOfInclusive is { } asOf) query = query.Where(x => x.t.OccurredOn <= asOf);

        return await query.SumAsync(x => (long?)x.p.AmountMinor, cancellationToken) ?? 0L;
    }

    public async Task<long> SubtreeBalanceAsync(
        string path, DateOnly? fromInclusive, DateOnly? toExclusive,
        CancellationToken cancellationToken = default)
    {
        var childPrefix = path + "/";

        var query = context.Postings
            .Join(context.Transactions, p => p.TransactionId, t => t.Id, (p, t) => new { p, t })
            .Join(context.Accounts, x => x.p.AccountId, a => a.Id, (x, a) => new { x.p, x.t, a })
            .Where(x => !x.t.IsVoided)
            .Where(x => x.a.Path == path || x.a.Path.StartsWith(childPrefix));

        if (fromInclusive is { } from) query = query.Where(x => x.t.OccurredOn >= from);
        if (toExclusive is { } to) query = query.Where(x => x.t.OccurredOn < to);

        return await query.SumAsync(x => (long?)x.p.AmountMinor, cancellationToken) ?? 0L;
    }

    public async Task<IReadOnlyList<AccountBalanceRow>> AllBalancesAsync(
        CancellationToken cancellationToken = default) =>
        await context.Postings
            .Join(context.Transactions, p => p.TransactionId, t => t.Id, (p, t) => new { p, t })
            .Where(x => !x.t.IsVoided)
            .GroupBy(x => x.p.AccountId)
            .Select(g => new AccountBalanceRow(g.Key, g.Sum(x => x.p.AmountMinor)))
            .ToListAsync(cancellationToken);

    public async Task<int> SubtreeEntryCountAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var childPrefix = path + "/";

        return await context.Postings
            .Join(context.Accounts, p => p.AccountId, a => a.Id, (p, a) => a)
            .CountAsync(a => a.Path == path || a.Path.StartsWith(childPrefix), cancellationToken);
    }

    public async Task<TransactionPage> ListAsync(
        TransactionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, 200);

        var transactions = context.Transactions.AsQueryable();

        if (!query.IncludeVoided) transactions = transactions.Where(t => !t.IsVoided);
        if (query.From is { } from) transactions = transactions.Where(t => t.OccurredOn >= from);
        if (query.To is { } to) transactions = transactions.Where(t => t.OccurredOn <= to);

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            transactions = transactions.Where(t =>
                EF.Functions.Like(t.Description, $"%{text}%")
                || (t.Payee != null && EF.Functions.Like(t.Payee, $"%{text}%")));
        }

        if (query.AccountId is { } accountId)
        {
            transactions = transactions.Where(t => t.Postings.Any(p => p.AccountId == accountId));
        }

        if (query.CategoryId is { } categoryId)
        {
            var category = await context.Accounts
                .Where(a => a.Id == categoryId)
                .Select(a => a.Path)
                .FirstOrDefaultAsync(cancellationToken);

            if (category is not null)
            {
                var prefix = category + "/";
                transactions = transactions.Where(t => t.Postings.Any(p =>
                    context.Accounts.Any(a => a.Id == p.AccountId
                                              && (a.Path == category || a.Path.StartsWith(prefix)))));
            }
        }

        if (DecodeCursor(query.Cursor) is { } cursor)
        {
            transactions = transactions.Where(t =>
                t.OccurredOn < cursor.OccurredOn
                || (t.OccurredOn == cursor.OccurredOn && t.Id.CompareTo(cursor.Id) < 0));
        }

        var page = await transactions
            .OrderByDescending(t => t.OccurredOn).ThenByDescending(t => t.Id)
            .Take(limit + 1)
            .Select(t => new
            {
                t.Id,
                t.OccurredOn,
                t.Description,
                t.Payee,
                t.IsVoided,
                Lines = t.Postings.Select(p => new
                {
                    p.AmountMinor,
                    p.CurrencyCode,
                    Kind = context.Accounts.First(a => a.Id == p.AccountId).Kind,
                    Name = context.Accounts.First(a => a.Id == p.AccountId).Name,
                    IsDeleted = context.Accounts.First(a => a.Id == p.AccountId).IsDeleted
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var rows = page.Take(limit).Select(t =>
        {
            var expenseLine = t.Lines.FirstOrDefault(l => l.Kind == Domain.Accounts.AccountKind.Expense);
            var assetLine = t.Lines.FirstOrDefault(l => l.Kind == Domain.Accounts.AccountKind.Asset);

            // The headline leg must be the same one named in the Account column, or the two
            // columns can describe different sides of the transaction. An expense line wins when
            // there is one (that is what makes a spend read as a plain positive number); otherwise
            // the asset leg does, since every template the user can reach (expense, income,
            // transfer, opening balance) always touches exactly one Kind=Asset account. Only a
            // transaction with neither - which no current template produces - falls back to an
            // arbitrary, but at least deterministic, largest-magnitude line.
            var headline = expenseLine ?? assetLine
                ?? t.Lines.OrderByDescending(l => Math.Abs(l.AmountMinor)).ThenBy(l => l.Name).First();

            return new TransactionRow(
                t.Id, t.OccurredOn, t.Description, t.Payee, t.IsVoided,
                headline.CurrencyCode, headline.AmountMinor, headline.Kind,
                expenseLine is null ? null : AccountDisplayName.For(expenseLine.Name, expenseLine.IsDeleted),
                assetLine is null ? null : AccountDisplayName.For(assetLine.Name, assetLine.IsDeleted));
        }).ToList();

        var nextCursor = hasMore && rows.Count > 0
            ? EncodeCursor(rows[^1].OccurredOn, rows[^1].Id)
            : null;

        return new TransactionPage(rows, nextCursor);
    }

    private static string EncodeCursor(DateOnly occurredOn, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            occurredOn.ToString("O", CultureInfo.InvariantCulture) + "|" + id.ToString("D")));

    private static (DateOnly OccurredOn, Guid Id)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2) return null;

            return (DateOnly.ParseExact(parts[0], "O", CultureInfo.InvariantCulture), Guid.Parse(parts[1]));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }
}
