using Microsoft.EntityFrameworkCore;
using Money.Api.Infrastructure;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Admin;
using Money.Application.Categories;
using Money.Application.FirstRun;
using Money.Application.Settings;
using Money.Application.Transactions;
using Money.Domain.Time;
using Money.Infrastructure.Backup;
using Money.Infrastructure.Export;
using Money.Infrastructure.Identity;
using Money.Infrastructure.Persistence;
using Money.Infrastructure.Persistence.Repositories;
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

        var connectionString = DataDirectory.ConnectionStringFor(
            DataDirectory.DatabasePathIn(dataDirectory));

        services.AddDbContext<MoneyDbContext>(options =>
            options.UseSqlite(connectionString).AddInterceptors(new SqlitePragmaInterceptor()));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICurrentUser, LocalCurrentUser>();
        services.AddSingleton(typeof(HostingMode), mode);

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<ILedgerQueries, LedgerQueries>();
        services.AddScoped<IIntegrityChecker, IntegrityChecker>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IdempotencyFilter>();

        services.AddScoped<IBackupService>(provider => new SqliteBackupService(
            provider.GetRequiredService<MoneyDbContext>(),
            new BackupOptions(dataDirectory, RetentionCount: 10),
            provider.GetRequiredService<IClock>()));

        services.AddScoped<JsonExportService>();
        services.AddScoped<CsvExportService>();
        services.AddScoped(provider => new ExportLedgerHandler(format => format switch
        {
            "json" => provider.GetRequiredService<JsonExportService>(),
            "csv" => provider.GetRequiredService<CsvExportService>(),
            _ => null
        }));

        services.AddScoped<DatabaseInitializer>();

        services.AddScoped<CreateAccountHandler>();
        services.AddScoped<PatchAccountHandler>();
        services.AddScoped<ArchiveAccountHandler>();
        services.AddScoped<ListAccountsHandler>();
        services.AddScoped<GetAccountBalanceHandler>();
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<GetCategoryTreeHandler>();
        services.AddScoped<CreateTransactionHandler>();
        services.AddScoped<GetTransactionHandler>();
        services.AddScoped<ReplaceTransactionHandler>();
        services.AddScoped<VoidTransactionHandler>();
        services.AddScoped<ListTransactionsHandler>();
        services.AddScoped<QuickExpenseHandler>();
        services.AddScoped<TransferHandler>();
        services.AddScoped<GetSettingsHandler>();
        services.AddScoped<UpdateSettingsHandler>();
        services.AddScoped<CompleteFirstRunSetupHandler>();
        services.AddScoped<CreateBackupHandler>();
        services.AddScoped<RunIntegrityCheckHandler>();

        return services;
    }
}
