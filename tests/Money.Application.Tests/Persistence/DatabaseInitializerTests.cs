using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Foreign_keys_are_on_for_every_connection()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";

        Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(1);
    }

    [Fact]
    public void Applying_the_migrations_twice_is_a_no_op()
    {
        using var context = _fixture.NewContext();

        var act = () => context.Database.Migrate();

        act.Should().NotThrow();
        context.Database.GetPendingMigrations().Should().BeEmpty();
    }

    [Fact]
    public void A_freshly_migrated_database_passes_the_integrity_check()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM (SELECT TransactionId FROM Postings " +
            "GROUP BY TransactionId HAVING SUM(AmountMinor) <> 0)";

        Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(0);
    }
}
