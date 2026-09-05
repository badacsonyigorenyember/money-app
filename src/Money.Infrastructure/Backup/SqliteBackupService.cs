using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Time;
using Money.Infrastructure.Persistence;

namespace Money.Infrastructure.Backup;

/// <summary>RetentionCount is only the fallback used before first-run setup exists a settings row.</summary>
public sealed record BackupOptions(string DataDirectory, int RetentionCount);

/// <summary>
/// Uses SQLite's Online Backup API, so a backup is consistent even while the app is running.
/// Copying the file with File.Copy would not be safe in WAL mode.
/// </summary>
public sealed class SqliteBackupService(
    MoneyDbContext context, BackupOptions options, ISettingsRepository settings, IClock clock)
    : IBackupService
{
    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var backupDirectory = DataDirectory.BackupDirectoryIn(options.DataDirectory);
        Directory.CreateDirectory(backupDirectory);

        var fileName = "money-"
            + clock.UtcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + ".db";
        var path = Path.Combine(backupDirectory, fileName);

        var source = (SqliteConnection)context.Database.GetDbConnection();
        var wasClosed = source.State != System.Data.ConnectionState.Open;
        if (wasClosed) source.Open();

        try
        {
            using var destination = new SqliteConnection($"DataSource={path};Pooling=False");
            destination.Open();
            source.BackupDatabase(destination);
        }
        finally
        {
            if (wasClosed) source.Close();
        }

        // Pooling=False above (not SqliteConnection.ClearAllPools) keeps this fix scoped to the
        // connection this method opened: without it, Microsoft.Data.Sqlite would keep a pooled
        // native handle on the just-written file after Dispose, and a later File.Delete of that
        // same file (once it falls out of retention) would throw IOException on Windows.
        // ClearAllPools() would fix the same symptom but reaches every pooled SQLite connection
        // in the process, forcing unrelated reconnects elsewhere - too broad for what is a
        // single connection's lifecycle problem.
        var retentionCount = (await settings.GetAsync(cancellationToken))?.BackupRetentionCount
            ?? options.RetentionCount;
        ApplyRetention(backupDirectory, retentionCount);
        return path;
    }

    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var backupDirectory = DataDirectory.BackupDirectoryIn(options.DataDirectory);
        if (!Directory.Exists(backupDirectory))
            return Task.FromResult<IReadOnlyList<BackupInfo>>([]);

        IReadOnlyList<BackupInfo> backups = Directory.EnumerateFiles(backupDirectory, "money-*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupInfo(f.Name, new DateTimeOffset(f.CreationTimeUtc, TimeSpan.Zero), f.Length))
            .ToArray();

        return Task.FromResult(backups);
    }

    private static void ApplyRetention(string backupDirectory, int retentionCount)
    {
        var keep = Math.Max(1, retentionCount);

        var stale = Directory.EnumerateFiles(backupDirectory, "money-*.db")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(keep)
            .ToArray();

        foreach (var path in stale) File.Delete(path);
    }
}
