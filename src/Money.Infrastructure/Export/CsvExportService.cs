using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Infrastructure.Persistence;

namespace Money.Infrastructure.Export;

/// <summary>
/// A flat entry table for spreadsheet use (spec section 8). Amounts are exported as minor
/// units, not decimals: a spreadsheet that reads "20.00" as a float is exactly the failure
/// mode spec D8 exists to prevent.
/// </summary>
public sealed class CsvExportService(MoneyDbContext context) : IExportService
{
    public Task<string> ExportJsonAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Use JsonExportService for JSON.");

    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Postings
            .Join(context.Transactions, p => p.TransactionId, t => t.Id, (p, t) => new { p, t })
            .Join(context.Accounts, x => x.p.AccountId, a => a.Id, (x, a) => new
            {
                x.t.Id,
                x.t.OccurredOn,
                x.t.Description,
                x.t.Payee,
                x.t.IsVoided,
                a.Path,
                AccountName = a.Name,
                x.p.AmountMinor,
                x.p.CurrencyCode,
                x.p.Memo
            })
            .OrderBy(r => r.OccurredOn).ThenBy(r => r.Id).ThenBy(r => r.Path)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.Append("TransactionId,OccurredOn,Description,Payee,IsVoided,AccountPath,AccountName,")
               .Append("AmountMinor,CurrencyCode,Memo\n");

        foreach (var row in rows)
        {
            builder
                .Append(Escape(row.Id.ToString())).Append(',')
                .Append(Escape(row.OccurredOn.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(row.Description)).Append(',')
                .Append(Escape(row.Payee)).Append(',')
                .Append(row.IsVoided ? "true" : "false").Append(',')
                .Append(Escape(row.Path)).Append(',')
                .Append(Escape(row.AccountName)).Append(',')
                .Append(row.AmountMinor.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.CurrencyCode)).Append(',')
                .Append(Escape(row.Memo)).Append('\n');
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var needsQuotes = value.IndexOfAny([',', '"', '\n', '\r']) >= 0;
        if (!needsQuotes) return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
