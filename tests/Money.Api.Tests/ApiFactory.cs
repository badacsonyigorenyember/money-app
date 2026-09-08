using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Money.Application.Abstractions;
using Money.Domain.Primitives;
using Money.Domain.Time;
using Money.Infrastructure.Persistence;
using Money.TestSupport;

namespace Money.Api.Tests;

/// <summary>
/// The whole app over a real, private SQLite database held in memory for the lifetime of the
/// factory, with a FakeClock so no test reads the system clock.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // AddMoneyApp (called from Program.cs, before ConfigureWebHost below gets a chance to swap
    // the DbContext for the in-memory one) resolves MONEYAPP_DATA_DIR and creates that directory
    // unconditionally. A static constructor runs exactly once per process, guaranteed by the CLR
    // to complete before the first ApiFactory instance (and therefore before the app's entry
    // point) can ever run, and before any second, concurrently-constructed ApiFactory could race
    // it under xUnit's parallel execution. Redirecting the variable here - once, for the whole
    // test process - means no test run ever touches the developer's real %APPDATA%/MoneyApp, not
    // even to create an empty folder in it.
    static ApiFactory()
    {
        var sandboxDataDirectory = Path.Combine(
            Path.GetTempPath(), "MoneyApp.Tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(DataDirectory.EnvironmentVariable, sandboxDataDirectory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(sandboxDataDirectory)) Directory.Delete(sandboxDataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a scratch temp directory; nothing depends on it existing.
            }
        };
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:;Foreign Keys=True");

    public FakeClock Clock { get; } = FakeClock.At(2026, 9, 1, 9, 0);

    /// <summary>Set before the first request to stand in for the real bank feed.</summary>
    public IBankFeed? BankFeed { get; init; }

    private sealed class UnusableBankFeed : IBankFeed
    {
        public Task<Result<IReadOnlyList<BankTransaction>>> FetchBookedAsync(
            DateOnly fromInclusive, DateOnly toInclusive, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "This test reached the bank feed. Set ApiFactory.BankFeed to a stub.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            _connection.Open();

            services.RemoveAll<DbContextOptions<MoneyDbContext>>();
            services.RemoveAll<MoneyDbContext>();
            services.AddDbContext<MoneyDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            // No test may reach the real Enable Banking API. A factory with no BankFeed set still
            // replaces it, so an accidental call fails loudly rather than going out over the wire.
            services.RemoveAll<IBankFeed>();
            services.AddSingleton(BankFeed ?? new UnusableBankFeed());

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<MoneyDbContext>().Database.Migrate();
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
