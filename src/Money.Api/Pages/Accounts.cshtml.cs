using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Accounts;
using Money.Application.Contracts;

namespace Money.Api.Pages;

public sealed class AccountsModel(
    ListAccountsHandler list,
    GetAccountBalanceHandler balances,
    CreateAccountHandler create,
    ArchiveAccountHandler archive) : PageModel
{
    public sealed record Row(AccountDto Account, decimal Balance);

    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)] public bool IncludeArchived { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] string name, [FromForm] string role, [FromForm] decimal? openingBalance,
        [FromForm] DateOnly? openedOn, CancellationToken cancellationToken)
    {
        var result = await create.HandleAsync(
            new CreateAccountRequest(name, "Asset", role, null, "EUR", openingBalance, openedOn),
            cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_AccountRows", this);
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await archive.HandleAsync(id, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_AccountRows", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        // Only the accounts a person thinks of as accounts. The Equity/OpeningBalance account is
        // bookkeeping and never appears here.
        var visible = (await list.HandleAsync("Asset", null, IncludeArchived, cancellationToken))
            .Where(a => a.Role is "Bank" or "Cash" or "SavingsPocket" or "Investment")
            .ToArray();

        var rows = new List<Row>(visible.Length);
        foreach (var account in visible)
        {
            var balance = await balances.HandleAsync(account.Id, null, cancellationToken);
            rows.Add(new Row(account, balance.IsSuccess ? balance.Value.Balance : 0m));
        }

        Rows = rows;
    }
}
