using Microsoft.EntityFrameworkCore;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Recurrence;

namespace Money.Infrastructure.Persistence;

public sealed class MoneyDbContext(DbContextOptions<MoneyDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Posting> Postings => Set<Posting>();
    public DbSet<SettingsEntity> Settings => Set<SettingsEntity>();
    public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MoneyDbContext).Assembly);
}
