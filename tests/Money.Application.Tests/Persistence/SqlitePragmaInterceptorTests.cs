using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.Infrastructure.Persistence;

namespace Money.Application.Tests.Persistence;

/// <summary>
/// Proves the interceptor itself, on a real connection - a test that only reads a pragma
/// already baked into a fixture's connection string (as <see cref="DatabaseInitializerTests"/>
/// does for foreign_keys) would pass even if the interceptor were deleted. journal_mode needs a
/// real file (an in-memory database always reports "memory"), so this uses a temp directory and
/// deletes it afterwards - never the real %APPDATA%.
/// </summary>
public sealed class SqlitePragmaInterceptorTests : IDisposable
{
    private readonly string _tempDirectory =
        Directory.CreateTempSubdirectory("moneyapp-pragma-test-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void All_three_pragmas_are_applied_when_a_connection_opens()
    {
        var databasePath = Path.Combine(_tempDirectory, "pragma-test.db");
        var options = new DbContextOptionsBuilder<MoneyDbContext>()
            .UseSqlite(DataDirectory.ConnectionStringFor(databasePath))
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        using var context = new MoneyDbContext(options);
        context.Database.OpenConnection();
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        ReadPragma(connection, "journal_mode").Should().Be("wal");
        ReadPragma(connection, "foreign_keys").Should().Be("1");
        ReadPragma(connection, "busy_timeout").Should().Be("5000");
    }

    private static string ReadPragma(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma}";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
