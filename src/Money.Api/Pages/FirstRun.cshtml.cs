using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Contracts;
using Money.Application.FirstRun;
using Money.Application.Settings;

namespace Money.Api.Pages;

public sealed class FirstRunModel(
    CompleteFirstRunSetupHandler complete, GetSettingsHandler settings) : PageModel
{
    [BindProperty] public string BaseCurrencyCode { get; set; } = "EUR";
    [BindProperty] public string PeriodAnchor { get; set; } = "CalendarMonth";
    [BindProperty] public int PeriodAnchorDay { get; set; } = 1;
    [BindProperty] public string TimeZoneId { get; set; } = "Europe/Budapest";
    [BindProperty] public string FirstDayOfWeek { get; set; } = "Monday";
    [BindProperty] public string FirstAccountName { get; set; } = "Current account";
    [BindProperty] public string FirstAccountRole { get; set; } = "Bank";
    [BindProperty] public decimal OpeningBalance { get; set; }
    [BindProperty] public DateOnly OpenedOn { get; set; }
    [BindProperty] public bool SeedStarterCategories { get; set; } = true;

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        (await settings.HandleAsync(cancellationToken)).FirstRunCompleted
            ? RedirectToPage("/Index")
            : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await complete.HandleAsync(new FirstRunRequest(
            BaseCurrencyCode, PeriodAnchor, PeriodAnchorDay, TimeZoneId, FirstDayOfWeek,
            FirstAccountName, FirstAccountRole, OpeningBalance, OpenedOn, SeedStarterCategories),
            cancellationToken);

        if (result.IsFailure)
        {
            ErrorMessage = result.Error!.Message;
            return Page();
        }

        return RedirectToPage("/Transactions");
    }
}
