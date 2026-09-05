using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Settings;

namespace Money.Api.Pages;

public sealed class IndexModel(GetSettingsHandler settings) : PageModel
{
    public bool FirstRunCompleted { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        FirstRunCompleted = (await settings.HandleAsync(cancellationToken)).FirstRunCompleted;
        return FirstRunCompleted ? Page() : RedirectToPage("/FirstRun");
    }
}
