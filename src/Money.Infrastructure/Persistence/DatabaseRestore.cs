using System.Globalization;
using Microsoft.Data.Sqlite;
using Money.Application.Admin;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// Restore happens in two moments, because a live app cannot swap the file it has open: WAL means
/// three files rather than one, and Microsoft.Data.Sqlite keeps pooled native handles - and with
/// them a Windows file lock - past <c>Dispose</c>. So a chosen backup is only validated and staged
/// while the app runs, and <see cref="ApplyPending"/> does the swap at the next start, before
/// anything has opened the database. Every connection opened here sets <c>Pooling=False</c> for
/// the same reason <see cref="Backup.SqliteBackupService"/> does.
/// </summary>
public sealed class DatabaseRestore(string dataDirectory) : IRestoreStaging
{
    public const string PendingFileName = "restore-pending.db";

    public static string PendingPathIn(string dataDirectory) =>
        Path.Combine(dataDirectory, PendingFileName);

    public string? FindBackup(string fileName)
    {
        var path = Path.Combine(DataDirectory.BackupDirectoryIn(dataDirectory), fileName);
        return File.Exists(path) ? path : null;
    }

    public bool IsRestorableDatabase(string backupPath)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();

            // The migrations history is what separates a backup of this app from any other SQLite
            // file: an empty one means a database that was created but never migrated, which would
            // restore as an empty ledger.
            return Scalar(connection, "PRAGMA integrity_check") as string == "ok"
                && Count(connection, "SELECT COUNT(*) FROM sqlite_master "
                    + "WHERE type = 'table' AND name = '__EFMigrationsHistory'") > 0
                && Count(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory") > 0;
        }
        catch (SqliteException)
        {
            // Not a database at all, or too damaged to answer either question.
            return false;
        }
    }

    public void Stage(string backupPath) =>
        File.Copy(backupPath, PendingPathIn(dataDirectory), overwrite: true);

    /// <summary>
    /// Applies a staged restore, if there is one. Must run before the first connection is opened.
    /// The database being replaced is backed up first: restoring the wrong backup would otherwise
    /// be the one unrecoverable action in the app.
    /// </summary>
    /// <returns>True when a restore was applied.</returns>
    public static bool ApplyPending(string dataDirectory, DateTimeOffset nowUtc)
    {
        var staged = PendingPathIn(dataDirectory);
        if (!File.Exists(staged)) return false;

        var live = DataDirectory.DatabasePathIn(dataDirectory);

        if (File.Exists(live))
        {
            BackUpBeforeRestore(live, dataDirectory, nowUtc);
            File.Delete(live);
        }

        // The sidecars belong to the database that just went; leaving them would let SQLite try to
        // recover them onto the restored file. Delete of a file that is not there is a no-op.
        File.Delete(live + "-wal");
        File.Delete(live + "-shm");

        File.Move(staged, live);
        return true;
    }

    private static void BackUpBeforeRestore(string live, string dataDirectory, DateTimeOffset nowUtc)
    {
        var directory = DataDirectory.BackupDirectoryIn(dataDirectory);
        Directory.CreateDirectory(directory);

        var fileName = "money-"
            + nowUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-before-restore.db";

        // SQLite's own backup API rather than File.Copy, so a -wal left behind by a crash is
        // folded in instead of silently dropped.
        using var source = new SqliteConnection($"DataSource={live};Pooling=False");
        using var destination = new SqliteConnection(
            $"DataSource={Path.Combine(directory, fileName)};Pooling=False");

        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static long Count(SqliteConnection connection, string sql) =>
        Convert.ToInt64(Scalar(connection, sql), CultureInfo.InvariantCulture);
}
