using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
///
/// Money.Api is a library, so there is no entry point for WebApplicationFactory to find and
/// invoke. The app is built here the same way Money.Desktop builds it - MoneyWebApp.CreateAsync,
/// naming Money.Api so the compiled Razor Pages are found - and served over a TestServer instead
/// of Kestrel.
/// </summary>
public sealed class ApiFactory : IDisposable
{
    // AddMoneyApp (called by MoneyWebApp.CreateAsync, before the configure callback below gets a
    // chance to swap the DbContext for the in-memory one) resolves MONEYAPP_DATA_DIR and creates
    // that directory unconditionally. A static constructor runs exactly once per process,
    // guaranteed by the CLR to complete before the first ApiFactory instance can ever run, and
    // before any second, concurrently-constructed ApiFactory could race it under xUnit's parallel
    // execution. Redirecting the variable here - once, for the whole test process - means no test
    // run ever touches the developer's real %APPDATA%/MoneyApp, not even to create an empty
    // folder in it.
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

    // Lazily, because xUnit constructs a class fixture eagerly and a test that never asks for the
    // app should not pay to boot one. WebApplicationFactory deferred the same way.
    private readonly Lazy<WebApplication> _app;

    public ApiFactory() => _app = new Lazy<WebApplication>(Start);

    public FakeClock Clock { get; } = FakeClock.At(2026, 9, 1, 9, 0);

    public IServiceProvider Services => _app.Value.Services;

    private WebApplication Start()
    {
        // Held open for the lifetime of the factory: a :memory: database lives exactly as long as
        // its first connection, so closing this one would erase it between requests.
        _connection.Open();

        var app = MoneyWebApp.CreateAsync(
            new WebApplicationOptions
            {
                ApplicationName = typeof(MoneyWebApp).Assembly.GetName().Name,
                EnvironmentName = "Testing",
                ContentRootPath = AppContext.BaseDirectory
            },
            builder =>
            {
                builder.WebHost.UseTestServer();

                builder.Services.RemoveAll<DbContextOptions<MoneyDbContext>>();
                builder.Services.RemoveAll<MoneyDbContext>();
                builder.Services.AddDbContext<MoneyDbContext>(options => options.UseSqlite(_connection));

                builder.Services.RemoveAll<IClock>();
                builder.Services.AddSingleton<IClock>(Clock);
            }).GetAwaiter().GetResult();

        // CreateAsync migrates the database itself on the way up, so the schema is already there.
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    /// <summary>
    /// A client that keeps cookies and does not follow redirects, which is what these tests want:
    /// a page that answers 302 is asserted on as a 302, not silently chased to wherever it
    /// points. TestServer does neither on its own.
    /// </summary>
    public HttpClient CreateApiClient()
    {
        var server = _app.Value.GetTestServer();
        return new HttpClient(new CookieHandler(server.CreateHandler()))
        {
            BaseAddress = server.BaseAddress
        };
    }

    /// <summary>
    /// The antiforgery token arrives as a cookie and has to come back on the next post, so a
    /// client that forgets it makes every form submission a 400. WebApplicationFactory supplied
    /// the equivalent; a bare TestServer has to be told.
    /// </summary>
    private sealed class CookieHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly CookieContainer _cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;

            var header = _cookies.GetCookieHeader(uri);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    _cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }

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

    public void Dispose()
    {
        if (_app.IsValueCreated)
        {
            _app.Value.StopAsync().GetAwaiter().GetResult();
            ((IDisposable)_app.Value).Dispose();
        }

        _connection.Dispose();
    }
}
