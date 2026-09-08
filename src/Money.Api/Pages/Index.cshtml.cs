using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Settings;

namespace Money.Api.Pages;

/// <summary>
/// The root is a signpost, not a page: a fresh database goes to the wizard, everything else goes
/// to the ledger, which is the home page.
/// </summary>
public sealed class IndexModel(GetSettingsHandler settings) : PageModel
{
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        (await settings.HandleAsync(cancellationToken)).FirstRunCompleted
            ? RedirectToPage("/Transactions")
            : RedirectToPage("/FirstRun");
}
