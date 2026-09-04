using Money.Application.Abstractions;
using Money.Domain.Primitives;

namespace Money.Application.Admin;

/// <summary>
/// Two classes implement IExportService, so this handler takes a resolver function rather than
/// the services themselves: keyed services with [FromKeyedServices] would put an ASP.NET Core
/// attribute in Money.Application, which the dependency-rule architecture test forbids. The
/// factory is supplied at composition time (Task 26).
/// </summary>
public sealed class ExportLedgerHandler(Func<string, IExportService?> resolve)
{
    public async Task<Result<(string Content, string ContentType, string FileName)>> HandleAsync(
        string? format, CancellationToken cancellationToken = default)
    {
        var normalised = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToLowerInvariant();

        var service = resolve(normalised);
        if (service is null) return DomainErrors.Admin.UnsupportedExportFormat(format ?? "");

        return normalised switch
        {
            "json" => Result<(string, string, string)>.Ok((
                await service.ExportJsonAsync(cancellationToken),
                "application/json", "money-export.json")),
            "csv" => Result<(string, string, string)>.Ok((
                await service.ExportCsvAsync(cancellationToken),
                "text/csv", "money-export.csv")),
            _ => DomainErrors.Admin.UnsupportedExportFormat(format ?? "")
        };
    }
}
