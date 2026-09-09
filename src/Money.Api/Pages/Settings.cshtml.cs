using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Admin;
using Money.Application.Contracts;
using Money.Application.Settings;
using Money.Domain.Money;

namespace Money.Api.Pages;

public sealed class SettingsModel(
    GetSettingsHandler get,
    UpdateSettingsHandler update,
    CreateBackupHandler backup,
    ListBackupsHandler listBackups,
    StageRestoreHandler stageRestore,
    IHostApplicationLifetime lifetime,
    RunIntegrityCheckHandler integrity) : PageModel
{
    public SettingsDto Current { get; private set; } = null!;
    public string? Message { get; private set; }
    public string? ErrorMessage { get; private set; }
    public IntegrityReportDto? Report { get; private set; }
    public IReadOnlyList<BackupResultDto> Backups { get; private set; } = [];

    [BindProperty] public UpdateSettingsRequest Form { get; set; } = null!;

    public static IReadOnlyList<string> CurrencyCodes { get; } =
        Currency.Known.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Time zones offered as IANA ids, which is what the settings row stores and what
    /// PeriodDefinition validates. Windows reports its own ids, so each is translated; any that
    /// will not translate is left out rather than saved in a form the next machine cannot read.
    /// </summary>
    public static IReadOnlyList<string> TimeZoneIds { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(zone => TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id)
        .Where(id => id.Contains('/', StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var result = await update.HandleAsync(Form, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else Message = "Saved.";

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostBackupAsync(CancellationToken cancellationToken)
    {
        var result = await backup.HandleAsync(cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else Message = $"Backup written: {result.Value.FileName}";

        await LoadAsync(cancellationToken);
        return Page();
    }

    /// <summary>
    /// Staging only: the file is swapped at the next start, before anything opens the database.
    /// Stopping the app is therefore part of the operation, not a side effect of it - the desktop
    /// host relaunches, and in server mode the operator restarts.
    /// </summary>
    public async Task<IActionResult> OnPostRestoreAsync(string? fileName, CancellationToken cancellationToken)
    {
        var result = stageRestore.Handle(fileName);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else Message = $"Restoring from {fileName}. The app is closing to finish the job — "
                     + "open it again and your data will be as it was in that backup.";

        await LoadAsync(cancellationToken);
        if (result.IsSuccess) lifetime.StopApplication();
        return Page();
    }

    public async Task<IActionResult> OnPostIntegrityCheckAsync(CancellationToken cancellationToken)
    {
        Report = await integrity.HandleAsync(cancellationToken);
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Current = await get.HandleAsync(cancellationToken);
        Backups = await listBackups.HandleAsync(cancellationToken);
        Form ??= new UpdateSettingsRequest(
            Current.BaseCurrencyCode, Current.PeriodAnchor, Current.PeriodAnchorDay,
            Current.TimeZoneId, Current.FirstDayOfWeek, Current.BackupRetentionCount);
    }
}
