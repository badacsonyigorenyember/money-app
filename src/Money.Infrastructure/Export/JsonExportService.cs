using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Infrastructure.Persistence;

namespace Money.Infrastructure.Export;

/// <summary>
/// A documented, re-importable shape (spec section 8). schemaVersion is bumped whenever the
/// shape changes so a future import path can tell what it is reading. Amounts are the raw
/// stored minor-unit values (positive = debit); re-importing does not need to un-negate
/// anything, because nothing here was negated for display.
/// </summary>
public sealed class JsonExportService(MoneyDbContext context) : IExportService
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<string> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await context.Accounts.OrderBy(a => a.Path)
            .Select(a => new
            {
                a.Id, a.Name, Kind = a.Kind.ToString(), Role = a.Role.ToString(),
                a.ParentAccountId, a.Path, a.CurrencyCode, a.IsArchived,
                a.OpenedOn, a.SortOrder, a.ColorHex, a.Icon, a.Notes
            })
            .ToListAsync(cancellationToken);

        var transactions = await context.Transactions
            .OrderBy(t => t.OccurredOn).ThenBy(t => t.Id)
            .Select(t => new
            {
                t.Id, t.OccurredOn, t.BookedAtUtc, t.Description, t.Payee,
                SourceKind = t.SourceKind.ToString(), t.SourceId, t.ExternalRef,
                t.IsVoided, t.VoidedAtUtc, t.VoidReason,
                Entries = t.Postings.Select(p => new
                {
                    p.Id, p.AccountId, p.AmountMinor, p.CurrencyCode, p.Memo
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var settings = await context.Settings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            accounts,
            transactions,
            settings
        }, Options);
    }

    public Task<string> ExportCsvAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Use CsvExportService for CSV.");
}
