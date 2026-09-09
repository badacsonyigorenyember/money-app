using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Money.Api.Infrastructure;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Admin;
using Money.Application.Categories;
using Money.Application.FirstRun;
using Money.Application.Import;
using Money.Application.Recurring;
using Money.Application.Settings;
using Money.Application.Transactions;
using Money.Domain.Time;
using Money.Infrastructure.Backup;
using Money.Infrastructure.Export;
using Money.Infrastructure.Identity;
using Money.Infrastructure.Import;
using Money.Infrastructure.Persistence;
using Money.Infrastructure.Persistence.Repositories;
using Money.Infrastructure.Rates;
using Money.Infrastructure.Time;

namespace Money.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddMoneyApp(
        this IServiceCollection services, IConfiguration configuration, HostingMode mode)
    {
        var dataDirectory = DataDirectory.Resolve(
            Environment.GetEnvironmentVariable(DataDirectory.EnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        Directory.CreateDirectory(dataDirectory);

        // A restore staged from the Settings screen is applied here, before AddDbContext and
        // therefore before anything can open the database: swapping the file under a live
        // connection is what makes restore the one operation that can destroy a ledger.
        DatabaseRestore.ApplyPending(dataDirectory, new SystemClock().UtcNow);

        var connectionString = DataDirectory.ConnectionStringFor(
            DataDirectory.DatabasePathIn(dataDirectory));

        services.AddDbContext<MoneyDbContext>(options =>
            options.UseSqlite(connectionString).AddInterceptors(new SqlitePragmaInterceptor()));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICurrentUser, LocalCurrentUser>();
        services.AddSingleton(typeof(HostingMode), mode);

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IRecurringRuleRepository, RecurringRuleRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<ILedgerQueries, LedgerQueries>();
        services.AddScoped<IIntegrityChecker, IntegrityChecker>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IdempotencyFilter>();

        services.AddScoped<IBackupService>(provider => new SqliteBackupService(
            provider.GetRequiredService<MoneyDbContext>(),
            new BackupOptions(dataDirectory, RetentionCount: 10),
            provider.GetRequiredService<ISettingsRepository>(),
            provider.GetRequiredService<IClock>()));

        services.AddSingleton<IRestoreStaging>(new DatabaseRestore(dataDirectory));

        services.AddScoped<JsonExportService>();
        services.AddScoped<CsvExportService>();
        services.AddScoped(provider => new ExportLedgerHandler(format => format switch
        {
            "json" => provider.GetRequiredService<JsonExportService>(),
            "csv" => provider.GetRequiredService<CsvExportService>(),
            _ => null
        }));

        services.AddScoped<DatabaseInitializer>();

        AddBankFeed(services, configuration);
        AddExchangeRates(services);

        services.AddScoped<CreateAccountHandler>();
        services.AddScoped<PatchAccountHandler>();
        services.AddScoped<ArchiveAccountHandler>();
        services.AddScoped<DeleteAccountHandler>();
        services.AddScoped<ListAccountsHandler>();
        services.AddScoped<GetAccountBalanceHandler>();
        services.AddScoped<GetAccountOverviewHandler>();
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<GetCategoryTreeHandler>();
        services.AddScoped<CreateTransactionHandler>();
        services.AddScoped<GetTransactionHandler>();
        services.AddScoped<ReplaceTransactionHandler>();
        services.AddScoped<VoidTransactionHandler>();
        services.AddScoped<ListTransactionsHandler>();
        services.AddScoped<QuickEntryHandler>();
        services.AddScoped<TransferHandler>();
        services.AddScoped<GetSettingsHandler>();
        services.AddScoped<UpdateSettingsHandler>();
        services.AddScoped<GetWindowStateHandler>();
        services.AddScoped<SaveWindowStateHandler>();
        services.AddScoped<CompleteFirstRunSetupHandler>();
        services.AddScoped<CreateBackupHandler>();
        services.AddScoped<ListBackupsHandler>();
        services.AddScoped<StageRestoreHandler>();
        services.AddScoped<RunIntegrityCheckHandler>();
        services.AddScoped<ImportBankTransactionsHandler>();
        services.AddScoped<CreateRecurringRuleHandler>();
        services.AddScoped<ListRecurringRulesHandler>();
        services.AddScoped<UpdateRecurringRuleHandler>();
        services.AddScoped<RecurringMaterialiser>();

        return services;
    }

    /// <summary>
    /// Frankfurter needs no key and no configuration, so there is nothing to switch on: the only
    /// thing worth deciding here is the lifetime. It is a singleton because the cache of fetched
    /// rate tables lives on the instance, and a per-request client would fetch once per page.
    /// </summary>
    private static void AddExchangeRates(IServiceCollection services)
    {
        services.AddHttpClient(FrankfurterExchangeRates.ClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.frankfurter.dev/");
            client.Timeout = TimeSpan.FromSeconds(8);
        });

        services.AddSingleton<IExchangeRates>(provider => new FrankfurterExchangeRates(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(
                FrankfurterExchangeRates.ClientName),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ILogger<FrankfurterExchangeRates>>()));
    }

    /// <summary>
    /// Enable Banking holds the PSD2 licence; this app is one of their applications, restricted
    /// to accounts the user linked themselves. The private key is read from a file rather than
    /// inlined in configuration so the PEM downloaded from their Control Panel can be pointed at
    /// directly and never lands in a committed settings file. A missing or unreadable key is not
    /// a startup failure: the feed simply reports itself unconfigured when asked.
    /// </summary>
    private static void AddBankFeed(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EnableBankingOptions.SectionName);
        var keyPath = section["PrivateKeyPath"];

        var pem = !string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath)
            ? File.ReadAllText(keyPath)
            : section["PrivateKeyPem"] ?? "";

        var options = new EnableBankingOptions(
            ApplicationId: section["ApplicationId"] ?? "",
            PrivateKeyPem: pem,
            SessionId: section["SessionId"] ?? "",
            AccountUid: section["AccountUid"] ?? "");

        services.AddSingleton(options);

        services.AddHttpClient<IBankFeed, EnableBankingClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.enablebanking.com");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
    }
}
