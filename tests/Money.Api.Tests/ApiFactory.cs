using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;
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

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<MoneyDbContext>().Database.Migrate();
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// Ledger setup and read-back through the app's own use cases. The desktop app exposes no
    /// JSON API - the window talks to page handlers, which talk to these - so a test that needs
    /// an account before it can look at a page asks exactly what a page would ask.
    /// </summary>
    public async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    public Task<AccountDto> CreateAccountAsync(
        string name, string role, string kind = "Asset", string? currencyCode = "EUR",
        decimal? openingBalance = null, DateOnly? openedOn = null) =>
        UseAsync(async services =>
        {
            var result = await services.GetRequiredService<CreateAccountHandler>().HandleAsync(
                new CreateAccountRequest(name, kind, role, null, currencyCode, openingBalance, openedOn));
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            return result.Value;
        });

    public Task<AccountDto> CreateCategoryAsync(string name, string kind, Guid? parentId = null) =>
        UseAsync(async services =>
        {
            var result = await services.GetRequiredService<CreateCategoryHandler>().HandleAsync(
                new CreateCategoryRequest(name, kind, parentId));
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            return result.Value;
        });

    public Task<TransactionDto> QuickEntryAsync(
        decimal amount, Guid categoryId, Guid accountId, DateOnly occurredOn, string description) =>
        UseAsync(async services =>
        {
            var result = await services.GetRequiredService<QuickEntryHandler>().HandleAsync(
                new QuickEntryRequest(amount, categoryId, accountId, occurredOn, description, null));
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            return result.Value;
        });

    public Task<decimal> BalanceAsync(Guid accountId) =>
        UseAsync(async services =>
        {
            var result = await services.GetRequiredService<GetAccountBalanceHandler>()
                .HandleAsync(accountId, null);
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            return result.Value.Balance;
        });

    public Task<TransactionPageDto> ListTransactionsAsync(string? text = null, bool includeVoided = false) =>
        UseAsync(services => services.GetRequiredService<ListTransactionsHandler>().HandleAsync(
            new TransactionQuery(null, null, null, null, text, includeVoided, null, 200)));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
