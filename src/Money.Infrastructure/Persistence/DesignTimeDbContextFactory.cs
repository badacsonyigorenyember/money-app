using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Money.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef`. Never resolved at runtime.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MoneyDbContext>
{
    public MoneyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MoneyDbContext>()
            .UseSqlite("DataSource=design-time.db")
            .Options);
}
