using Money.Application.Abstractions;
using Money.Application.Contracts;

namespace Money.Application.Admin;

/// <summary>Newest first, which is the order the backup service already lists them in.</summary>
public sealed class ListBackupsHandler(IBackupService backups)
{
    public async Task<IReadOnlyList<BackupResultDto>> HandleAsync(
        CancellationToken cancellationToken = default) =>
        (await backups.ListBackupsAsync(cancellationToken))
            .Select(backup => new BackupResultDto(backup.FileName, backup.CreatedAtUtc, backup.SizeBytes))
            .ToArray();
}
