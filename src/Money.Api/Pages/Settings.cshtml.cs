using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Admin;
using Money.Application.Contracts;
using Money.Application.Settings;

namespace Money.Api.Pages;

public sealed class SettingsModel(
    GetSettingsHandler get,
    UpdateSettingsHandler update,
    CreateBackupHandler backup,
    RunIntegrityCheckHandler integrity) : PageModel
{
    public SettingsDto Current { get; private set; } = null!;
    public string? Message { get; private set; }
    public string? ErrorMessage { get; private set; }
    public IntegrityReportDto? Report { get; private set; }

    [BindProperty] public UpdateSettingsRequest Form { get; set; } = null!;

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

    public async Task<IActionResult> OnPostIntegrityCheckAsync(CancellationToken cancellationToken)
    {
        Report = await integrity.HandleAsync(cancellationToken);
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Current = await get.HandleAsync(cancellationToken);
        Form ??= new UpdateSettingsRequest(
            Current.BaseCurrencyCode, Current.PeriodAnchor, Current.PeriodAnchorDay,
            Current.TimeZoneId, Current.FirstDayOfWeek, Current.BackupRetentionCount);
    }
}
