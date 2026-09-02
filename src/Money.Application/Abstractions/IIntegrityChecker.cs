namespace Money.Application.Abstractions;

public sealed record IntegrityFinding(string Check, string Detail);

public sealed record IntegrityReport(bool IsHealthy, IReadOnlyList<IntegrityFinding> Findings);

public interface IIntegrityChecker
{
    Task<IntegrityReport> CheckAsync(CancellationToken cancellationToken = default);
}
