namespace Money.Application.Contracts;

public sealed record BackupResultDto(string FileName, DateTimeOffset CreatedAtUtc, long SizeBytes);

public sealed record IntegrityReportDto(bool IsHealthy, IReadOnlyList<string> Findings);
