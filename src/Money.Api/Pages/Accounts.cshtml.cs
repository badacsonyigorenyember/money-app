using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Contracts;

namespace Money.Api.Pages;

public sealed class AccountsModel(
    ListAccountsHandler list,
    GetAccountBalanceHandler balances,
    CreateAccountHandler create,
    ArchiveAccountHandler archive,
    DeleteAccountHandler delete,
    ILedgerQueries queries) : PageModel
{
    /// <summary><paramref name="Entries"/> is only counted for archived rows: it is what decides
    /// whether deleting can erase the account or has to keep its name for the history that uses
    /// it, and an account still in use is never offered a delete button.</summary>
    public sealed record Row(AccountDto Account, decimal Balance, int Entries);

    public IReadOnlyList<Row> Open { get; private set; } = [];
    public IReadOnlyList<Row> Archived { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public string? Message { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] string name, [FromForm] string role, [FromForm] decimal? openingBalance,
        [FromForm] DateOnly? openedOn, CancellationToken cancellationToken)
    {
        var result = await create.HandleAsync(
            new CreateAccountRequest(name, "Asset", role, null, null, openingBalance, openedOn),
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

        if (result.IsSuccess)
        {
            var archived = Archived.FirstOrDefault(row => row.Account.Id == id);
            Message = archived is { Entries: 0 }
                ? $"{archived.Account.Name} is archived. Nothing in your history uses it, " +
                  "so deleting it will erase it completely."
                : "Archived. It is out of the way but its history is intact.";
        }

        return Partial("Shared/_AccountRows", this);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // Read the name before the delete: afterwards there may be no row left to read it from.
        await LoadAsync(cancellationToken);
        var name = Archived.FirstOrDefault(row => row.Account.Id == id)?.Account.Name;

        var result = await delete.HandleAsync(id, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else Message = $"{name ?? "The account"} has been deleted.";

        await LoadAsync(cancellationToken);
        return Partial("Shared/_AccountRows", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        // Only the accounts a person thinks of as accounts. The Equity/OpeningBalance account is
        // bookkeeping and never appears here.
        var visible = (await list.HandleAsync("Asset", null, includeArchived: true, cancellationToken))
            .Where(a => a.Role is "Bank" or "Cash" or "SavingsPocket" or "Investment")
            .ToArray();

        var open = new List<Row>();
        var archived = new List<Row>();

        foreach (var account in visible)
        {
            var balance = await balances.HandleAsync(account.Id, null, cancellationToken);
            var amount = balance.IsSuccess ? balance.Value.Balance : 0m;

            if (account.IsArchived)
            {
                archived.Add(new Row(
                    account, amount, await queries.SubtreeEntryCountAsync(account.Path, cancellationToken)));
            }
            else
            {
                open.Add(new Row(account, amount, 0));
            }
        }

        Open = open;
        Archived = archived;
    }
}
