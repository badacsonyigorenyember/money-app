using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.Infrastructure.Persistence;

namespace Money.TestSupport;

/// <summary>
/// A real SQLite database in memory, with the connection held open for its lifetime and all
/// migrations applied. An in-memory EF *provider* is deliberately not used: only real SQLite
/// runs the check constraints and unique indexes that carry part of the safety argument.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    public SqliteFixture()
    {
        Connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        Connection.Open();

        using var context = NewContext();
        context.Database.Migrate();
    }

    public SqliteConnection Connection { get; }

    public MoneyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<MoneyDbContext>().UseSqlite(Connection).Options);

    public void Dispose() => Connection.Dispose();
}
