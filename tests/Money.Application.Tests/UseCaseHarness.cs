using Money.Application.Abstractions;
using Money.Infrastructure.Persistence;
using Money.Infrastructure.Persistence.Repositories;
using Money.TestSupport;

namespace Money.Application.Tests;

/// <summary>
/// Real SQLite, real repositories, a fake clock. No mocks: the point of the application tests
/// is that constraints and SQL actually run.
/// </summary>
public sealed class UseCaseHarness : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public UseCaseHarness()
    {
        Context = _fixture.NewContext();
        Accounts = new AccountRepository(Context);
        Transactions = new TransactionRepository(Context);
        RecurringRules = new RecurringRuleRepository(Context);
        Settings = new SettingsRepository(Context);
        Queries = new LedgerQueries(Context);
        UnitOfWork = new EfUnitOfWork(Context);
    }

    public MoneyDbContext Context { get; }
    public IAccountRepository Accounts { get; }
    public ITransactionRepository Transactions { get; }
    public IRecurringRuleRepository RecurringRules { get; }
    public ISettingsRepository Settings { get; }
    public ILedgerQueries Queries { get; }
    public IUnitOfWork UnitOfWork { get; }
    public FakeClock Clock { get; } = FakeClock.At(2026, 9, 1, 9, 0);

    public void Dispose()
    {
        Context.Dispose();
        _fixture.Dispose();
    }
}
