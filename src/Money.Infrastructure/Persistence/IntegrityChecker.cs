using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// The zero-sum invariant (I1) cannot be expressed as a SQLite row check, so it is verified by
/// this sweep at startup and on demand from the admin screen (spec section 8).
/// </summary>
public sealed class IntegrityChecker(MoneyDbContext context) : IIntegrityChecker
{
    public async Task<IntegrityReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<IntegrityFinding>();

        var unbalanced = await context.Postings
            .GroupBy(p => p.TransactionId)
            .Where(g => g.Sum(p => p.AmountMinor) != 0)
            .Select(g => g.Key)
            .Take(50)
            .ToListAsync(cancellationToken);

        findings.AddRange(unbalanced.Select(id =>
            new IntegrityFinding("zero-sum", $"Transaction {id} does not sum to zero.")));

        var tooFewEntries = await context.Postings
            .GroupBy(p => p.TransactionId)
            .Where(g => g.Count() < 2)
            .Select(g => g.Key)
            .Take(50)
            .ToListAsync(cancellationToken);

        findings.AddRange(tooFewEntries.Select(id =>
            new IntegrityFinding("entry-count", $"Transaction {id} has fewer than two entries.")));

        var orphanPostings = await context.Postings
            .Where(p => !context.Accounts.Any(a => a.Id == p.AccountId))
            .Select(p => p.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        findings.AddRange(orphanPostings.Select(id =>
            new IntegrityFinding("orphan-entry", $"Entry {id} references a missing account.")));

        var brokenPaths = await context.Accounts
            .Where(a => a.ParentAccountId != null
                        && !context.Accounts.Any(p => p.Id == a.ParentAccountId
                                                      && a.Path.StartsWith(p.Path + "/")))
            .Select(a => a.Path)
            .Take(50)
            .ToListAsync(cancellationToken);

        findings.AddRange(brokenPaths.Select(path =>
            new IntegrityFinding("account-path", $"Account path '{path}' does not match its parent.")));

        return new IntegrityReport(findings.Count == 0, findings);
    }
}
