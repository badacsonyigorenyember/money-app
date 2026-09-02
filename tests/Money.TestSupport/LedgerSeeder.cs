using Microsoft.EntityFrameworkCore;
using Money.Infrastructure.Persistence;

namespace Money.TestSupport;

public static class LedgerSeeder
{
    public static async Task<LedgerGen.GeneratedLedger> SeedAsync(
        MoneyDbContext context, LedgerGen.GeneratedLedger ledger,
        CancellationToken cancellationToken = default)
    {
        context.Accounts.AddRange(ledger.Accounts);
        context.Transactions.AddRange(ledger.Transactions);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return ledger;
    }
}
