using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Time;
using Money.Infrastructure.Persistence;

namespace Money.Infrastructure.Backup;

public sealed record BackupOptions(string DataDirectory, int RetentionCount);

/// <summary>
/// Uses SQLite's Online Backup API, so a backup is consistent even while the app is running.
/// Copying the file with File.Copy would not be safe in WAL mode.
/// </summary>
public sealed class SqliteBackupService(MoneyDbContext context, BackupOptions options, IClock clock)
    : IBackupService
{
    public Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
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
            using var destination = new SqliteConnection($"DataSource={path}");
            destination.Open();
            source.BackupDatabase(destination);
        }
        finally
        {
            if (wasClosed) source.Close();
        }

        // Microsoft.Data.Sqlite pools the native connection even after Dispose, which would
        // otherwise leave the just-written file (and any older backup file previously opened
        // the same way) locked on Windows when retention tries to delete it.
        SqliteConnection.ClearAllPools();

        ApplyRetention(backupDirectory);
        return Task.FromResult(path);
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

    private void ApplyRetention(string backupDirectory)
    {
        var keep = Math.Max(1, options.RetentionCount);

        var stale = Directory.EnumerateFiles(backupDirectory, "money-*.db")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(keep)
            .ToArray();

        foreach (var path in stale) File.Delete(path);
    }
}
