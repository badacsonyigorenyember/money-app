using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;
using Money.Domain.Time;

namespace Money.Api.Pages;

public sealed class TransactionsModel(
    ListTransactionsHandler list,
    QuickExpenseHandler quickExpense,
    VoidTransactionHandler voidTransaction,
    GetCategoryTreeHandler categories,
    ListAccountsHandler accounts,
    ISettingsRepository settingsRepository,
    IClock clock) : PageModel
{
    public new TransactionPageDto Page { get; private set; } = new([], null);
    public IReadOnlyList<AccountDto> Accounts { get; private set; } = [];
    public IReadOnlyList<CategoryNodeDto> Categories { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public DateOnly Today { get; private set; }

    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? AccountId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CategoryId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool IncludeVoided { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostQuickAddAsync(
        [FromForm] decimal amount, [FromForm] Guid categoryId, [FromForm] Guid accountId,
        [FromForm] DateOnly? occurredOn, [FromForm] string? description,
        CancellationToken cancellationToken)
    {
        var result = await quickExpense.HandleAsync(
            new QuickExpenseRequest(amount, categoryId, accountId, occurredOn, description, null),
            cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_TransactionRows", this);
    }

    public async Task<IActionResult> OnPostVoidAsync(
        Guid id, string reason, CancellationToken cancellationToken)
    {
        var result = await voidTransaction.HandleAsync(
            id, string.IsNullOrWhiteSpace(reason) ? "Removed by the user" : reason, cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_TransactionRows", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        // The quick-add row's date defaults to today in the user's configured zone, never to
        // DateTime.Today - which would put a late-evening expense in yesterday.
        Today = await TodayResolver.TodayAsync(settingsRepository, clock, cancellationToken);

        Page = await list.HandleAsync(
            new TransactionQuery(From, To, AccountId, CategoryId, Q, IncludeVoided, null, 50),
            cancellationToken);

        Accounts = (await accounts.HandleAsync(null, null, false, cancellationToken))
            .Where(a => a.Kind == "Asset").ToArray();

        Categories = await categories.HandleAsync("Expense", false, cancellationToken);
    }
}
