using System.Diagnostics.CodeAnalysis;
using Money.Domain.Primitives;

namespace Money.Application.Admin;

/// <summary>
/// The file-system half of staging a restore. A port because opening a candidate as SQLite is
/// infrastructure, and this layer may not reference it.
/// </summary>
public interface IRestoreStaging
{
    /// <summary>Full path of the named file inside the backups folder, or null when it is not there.</summary>
    string? FindBackup(string fileName);

    /// <summary>True when the file opens as SQLite, passes its integrity check, and carries a migration history.</summary>
    bool IsRestorableDatabase(string backupPath);

    /// <summary>Copies the file into the pending-restore slot. The live database is not touched.</summary>
    void Stage(string backupPath);
}

/// <summary>
/// Stages a restore for the next start. Nothing is swapped here: a live app cannot replace the
/// database file it has open, so this only validates the candidate and puts it where startup
/// looks for it (spec section 8).
/// </summary>
public sealed class StageRestoreHandler(IRestoreStaging staging)
{
    public Result Handle(string? fileName)
    {
        if (!IsPlainFileName(fileName)) return DomainErrors.Admin.InvalidBackupName(fileName ?? "");

        var path = staging.FindBackup(fileName);
        if (path is null) return DomainErrors.Admin.BackupNotFound(fileName);
        if (!staging.IsRestorableDatabase(path)) return DomainErrors.Admin.BackupNotRestorable(fileName);

        staging.Stage(path);
        return Result.Ok();
    }

    // Spelled out rather than checked by round-tripping through Path.GetFileName, because what
    // counts as a separator is platform-dependent: '\' is an ordinary character in a Linux file
    // name, and CI runs on Linux. A name is either plain everywhere or it is refused.
    private static bool IsPlainFileName([NotNullWhen(true)] string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && !fileName.Contains("..", StringComparison.Ordinal)
        && fileName.IndexOfAny(['/', '\\', ':']) < 0;
}
