using Microsoft.Data.Sqlite;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// Where the ledger lives. Deliberately NOT beside the executable: the project folder is on the
/// Desktop, which is commonly OneDrive-synced, and a synced SQLite file in WAL mode can be
/// corrupted by the sync client (spec D10 and section 14).
/// </summary>
public static class DataDirectory
{
    public const string EnvironmentVariable = "MONEYAPP_DATA_DIR";

    public const string DatabaseFileName = "money.db";

    public const string BackupFolderName = "backups";

    public static string Resolve(string? overrideDirectory, string appDataRoot) =>
        string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(appDataRoot, "MoneyApp")
            : overrideDirectory.Trim();

    public static string DatabasePathIn(string dataDirectory) =>
        Path.Combine(dataDirectory, DatabaseFileName);

    public static string BackupDirectoryIn(string dataDirectory) =>
        Path.Combine(dataDirectory, BackupFolderName);

    public static string ConnectionStringFor(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default
        }.ToString();
}
