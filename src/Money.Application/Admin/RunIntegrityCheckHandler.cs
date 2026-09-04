using Money.Application.Abstractions;
using Money.Application.Contracts;

namespace Money.Application.Admin;

public sealed class RunIntegrityCheckHandler(IIntegrityChecker checker)
{
    public async Task<IntegrityReportDto> HandleAsync(CancellationToken cancellationToken = default)
    {
        var report = await checker.CheckAsync(cancellationToken);
        return new IntegrityReportDto(
            report.IsHealthy, report.Findings.Select(f => $"{f.Check}: {f.Detail}").ToArray());
    }
}
