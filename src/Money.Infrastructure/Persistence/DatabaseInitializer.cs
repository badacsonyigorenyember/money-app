using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// Startup sequence: back up first, then migrate. Spec section 8 - a migration that goes wrong
/// must never be the only copy of the ledger.
/// </summary>
public sealed partial class DatabaseInitializer(
    MoneyDbContext context,
    IBackupService backupService,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

        if (pending.Length > 0 && await context.Database.CanConnectAsync(cancellationToken))
        {
            var alreadyApplied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
            if (alreadyApplied.Any())
            {
                var path = await backupService.CreateBackupAsync(cancellationToken);
                LogBackupWritten(logger, path);
            }
        }

        await context.Database.MigrateAsync(cancellationToken);

        if (pending.Length > 0)
        {
            LogMigrationsApplied(logger, pending.Length, string.Join(", ", pending));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pre-migration backup written to {BackupPath}")]
    private static partial void LogBackupWritten(ILogger logger, string backupPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migration(s): {Migrations}")]
    private static partial void LogMigrationsApplied(ILogger logger, int count, string migrations);
}
