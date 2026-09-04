using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Domain.Primitives;

namespace Money.Application.Admin;

public sealed class CreateBackupHandler(IBackupService backups)
{
    public async Task<Result<BackupResultDto>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = await backups.CreateBackupAsync(cancellationToken);
            var info = (await backups.ListBackupsAsync(cancellationToken))
                .FirstOrDefault(b => b.FileName == Path.GetFileName(path));

            return info is null
                ? DomainErrors.Admin.BackupFailed("the backup file could not be found after writing")
                : Result<BackupResultDto>.Ok(
                    new BackupResultDto(info.FileName, info.CreatedAtUtc, info.SizeBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DomainErrors.Admin.BackupFailed(ex.Message);
        }
    }
}
