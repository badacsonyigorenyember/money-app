namespace Money.Application.Abstractions;

public interface IExportService
{
    Task<string> ExportJsonAsync(CancellationToken cancellationToken = default);

    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);
}
