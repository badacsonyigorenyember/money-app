namespace Money.Application.Abstractions;

public sealed record BackupInfo(string FileName, DateTimeOffset CreatedAtUtc, long SizeBytes);

public interface IBackupService
{
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);
}
