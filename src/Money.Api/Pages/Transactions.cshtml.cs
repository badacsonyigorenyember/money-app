using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Recurring;
using Money.Application.Transactions;
using Money.Domain.Time;

namespace Money.Api.Pages;

public sealed class TransactionsModel(
    ListTransactionsHandler list,
    QuickEntryHandler quickEntry,
    TransferHandler transfer,
    VoidTransactionHandler voidTransaction,
    GetCategoryTreeHandler categories,
    ListAccountsHandler accounts,
    GetAccountOverviewHandler overview,
    CreateRecurringRuleHandler createRule,
    ListRecurringRulesHandler listRules,
    UpdateRecurringRuleHandler updateRule,
    RecurringMaterialiser materialiser,
    ISettingsRepository settingsRepository,
    IClock clock) : PageModel
{
    public new TransactionPageDto Page { get; private set; } = new([], null);
    public IReadOnlyList<AccountDto> Accounts { get; private set; } = [];
    public MonthOverviewDto Overview { get; private set; } =
        new("", "", default, default, 0, "EUR", "", null, []);
    public IReadOnlyList<CategoryNodeDto> SpendCategories { get; private set; } = [];
    public IReadOnlyList<CategoryNodeDto> IncomeCategories { get; private set; } = [];
    public IReadOnlyList<RecurringRuleDto> Rules { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public string? RepeatMessage { get; private set; }
    public DateOnly Today { get; private set; }

    /// <summary>
    /// True when the repeating-rules list is being sent as an HTMX out-of-band update rather than
    /// rendered in its own place on the page. Adding a rule swaps the history table, and the
    /// rules list sits in a different section, so it rides along on the same response.
    /// </summary>
    public bool RulesOutOfBand { get; private set; }

    /// <summary>
    /// Which month the page is showing, as a period key such as <c>2026-M09</c>. One value drives
    /// both the chart and the entry list, so the arrows above the list can never leave the two
    /// looking at different months. Absent means the month in progress.
    /// </summary>
    // FromQuery pins these to the query string. Without it they would also bind from a posted
    // form, and the quick-add form's own categoryId and accountId fields would silently become
    // filters - so recording one thing would hide everything else from the refreshed list.
    [BindProperty(SupportsGet = true), FromQuery] public string? Month { get; set; }

    /// <summary>
    /// Which accounts to draw. Ticking a box re-asks the server rather than hiding a line in the
    /// browser, because the y axis is shared: hiding the largest account client-side would leave
    /// the rest squashed against the floor at a scale nothing on screen still needs.
    /// </summary>
    [BindProperty(SupportsGet = true), FromQuery] public Guid[]? Selected { get; set; }

    /// <summary>
    /// Set by the picker itself, so "every box unticked" can be told apart from "the picker has
    /// not been touched" - both of which arrive as an absent <see cref="Selected"/>.
    /// </summary>
    [BindProperty(SupportsGet = true), FromQuery] public bool Picked { get; set; }

    /// <summary>
    /// The accounts actually drawn: those ticked, converted to the ledger's base currency so one
    /// shared axis can carry all of them. An account with no rate to convert by - an unquoted
    /// currency, or no network yet - keeps its row in the figures below and stays off the chart,
    /// because a line drawn without a rate would be a number nobody has.
    /// </summary>
    public IReadOnlyList<AccountMonthDto> Charted =>
        Overview.Accounts
            .Where(a => a.RateToBase is not null)
            .Where(a => !Picked || (Selected ?? []).Contains(a.AccountId))
            .ToArray();

    /// <summary>
    /// Which column orders the list, as a key with an optional leading <c>-</c> for descending:
    /// <c>-date</c> is the default the list has always had. Anything else is ignored rather than
    /// rejected - an order is not worth an error page.
    /// </summary>
    [BindProperty(SupportsGet = true), FromQuery] public string? Sort { get; set; }

    private static readonly string[] Sortable = ["date", "description", "category", "account", "amount"];

    private string Ordering =>
        Sortable.Contains((Sort ?? "").TrimStart('-')) ? Sort! : "-date";

    public string SortKey => Ordering.TrimStart('-');
    public bool SortDescending => Ordering.StartsWith('-');

    /// <summary>The ordering as it goes back into a query string, so a swap keeps it.</summary>
    public string CurrentSort => Ordering;

    /// <summary>Where a click on <paramref name="key"/>'s heading goes: that column the other way
    /// round if it is the one already ordering the list, and ascending if it is not.</summary>
    public string NextSort(string key) => SortKey == key && !SortDescending ? "-" + key : key;

    public string AriaSort(string key) =>
        SortKey != key ? "none" : SortDescending ? "descending" : "ascending";

    [BindProperty(SupportsGet = true), FromQuery] public Guid? AccountId { get; set; }
    [BindProperty(SupportsGet = true), FromQuery] public Guid? CategoryId { get; set; }
    [BindProperty(SupportsGet = true), FromQuery] public string? Q { get; set; }
    [BindProperty(SupportsGet = true), FromQuery] public bool IncludeVoided { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Catching up here as well as at startup is what makes a long-running window still post
        // this morning's salary. A run with nothing due writes nothing.
        await materialiser.RunAsync(cancellationToken);
        await LoadAsync(cancellationToken);
    }

    /// <summary>Redraws the chart alone when the account picker changes.</summary>
    public async Task<IActionResult> OnGetChartAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Partial("Shared/_AccountOverview", this);
    }

    /// <summary>
    /// The category picker's one non-category choice. Moving money between two accounts is not a
    /// category - nothing is spent or earned - but it is recorded from the same sheet, so it rides
    /// in the same field rather than in a mode of its own.
    /// </summary>
    public const string MoveMoney = "move";

    public async Task<IActionResult> OnPostQuickAddAsync(
        [FromForm] decimal amount, [FromForm] string? categoryId, [FromForm] Guid accountId,
        [FromForm] Guid fromAccountId, [FromForm] Guid toAccountId,
        [FromForm] DateOnly? occurredOn, [FromForm] string? description,
        [FromForm] bool repeat, [FromForm] RepeatForm? every,
        CancellationToken cancellationToken)
    {
        if (categoryId == MoveMoney)
        {
            // Never repeating: a schedule is written as income or expense against a category, and
            // a move has neither. The panel is hidden for this choice, and ignored if it arrives.
            var moved = await transfer.HandleAsync(
                new TransferRequest(amount, fromAccountId, toAccountId, occurredOn, description),
                cancellationToken);

            if (moved.IsFailure) ErrorMessage = moved.Error!.Message;
        }
        else if (!Guid.TryParse(categoryId, out var category))
        {
            ErrorMessage = "Choose what this was for.";
        }
        else if (repeat)
        {
            await AddRepeatingAsync(
                amount, category, accountId, occurredOn, description,
                every ?? new RepeatForm(), cancellationToken);
        }
        else
        {
            var result = await quickEntry.HandleAsync(
                new QuickEntryRequest(amount, category, accountId, occurredOn, description, null),
                cancellationToken);

            if (result.IsFailure) ErrorMessage = result.Error!.Message;
        }

        return await RowsAsync(cancellationToken);
    }

    private async Task AddRepeatingAsync(
        decimal amount, Guid categoryId, Guid accountId, DateOnly? occurredOn, string? description,
        RepeatForm every, CancellationToken cancellationToken)
    {
        var startDate = occurredOn
            ?? await TodayResolver.TodayAsync(settingsRepository, clock, cancellationToken);

        // Which way the money goes is read off the chosen category, exactly as a one-off entry
        // reads it - so "Salary" repeats as income without the form asking a second question.
        var isIncome = (await categories.HandleAsync("Income", false, cancellationToken))
            .SelectMany(Flatten)
            .Any(node => node.Id == categoryId);

        var direction = isIncome ? "Income" : "Expense";

        var created = await createRule.HandleAsync(new CreateRecurringRuleRequest(
            direction, amount, accountId, categoryId, description, null,
            every.Frequency, every.Interval,
            every.DayOfWeek?.ToString(), every.MoveOffWeekends, every.DayOfMonth, every.Month,
            every.Years, every.Months, every.Days,
            startDate, every.EndDate), cancellationToken);

        if (created.IsFailure)
        {
            ErrorMessage = created.Error!.Message;
            return;
        }

        // Post whatever the new rule already owes, so a salary dated today shows up immediately
        // rather than at the next page load.
        await materialiser.RunAsync(cancellationToken);

        RepeatMessage = created.Value.NextOccurrence is { } next
            ? $"{created.Value.Description} now repeats: {created.Value.ScheduleSummary.ToLowerInvariant()}. " +
              $"Next on {next:yyyy-MM-dd}."
            : $"{created.Value.Description} now repeats: {created.Value.ScheduleSummary.ToLowerInvariant()}.";
    }

    private static IEnumerable<CategoryNodeDto> Flatten(CategoryNodeDto node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));

    public async Task<IActionResult> OnPostVoidAsync(
        Guid id, string reason, CancellationToken cancellationToken)
    {
        var result = await voidTransaction.HandleAsync(
            id, string.IsNullOrWhiteSpace(reason) ? "Removed by the user" : reason, cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        return await RowsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPauseRepeatAsync(
        Guid id, bool active, CancellationToken cancellationToken)
    {
        var result = await updateRule.SetActiveAsync(id, active, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else if (active) await materialiser.RunAsync(cancellationToken);

        return await RowsAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the rule and forgets it. The entries it already posted stay: they are history, and
    /// history is voided one entry at a time, never deleted wholesale.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteRepeatAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await updateRule.DeleteAsync(id, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        return await RowsAsync(cancellationToken);
    }

    private async Task<IActionResult> RowsAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        RulesOutOfBand = true;
        return Partial("Shared/_TransactionRows", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        // The quick-add row's date defaults to today in the user's configured zone, never to
        // DateTime.Today - which would put a late-evening expense in yesterday.
        Today = await TodayResolver.TodayAsync(settingsRepository, clock, cancellationToken);

        // Resolved first: the chart and the entry list below it read the same month, and it is the
        // overview that decides which month a key means (and what "the month in progress" is).
        Overview = await overview.HandleAsync(Month, cancellationToken);
        Month = Overview.PeriodKey;

        Page = await list.HandleAsync(
            new TransactionQuery(
                Overview.Start, Overview.EndInclusive,
                // No cursor, no ceiling: the month is the page, so the table is as tall as the
                // month is busy.
                AccountId, CategoryId, Q, IncludeVoided, null, int.MaxValue),
            cancellationToken);

        Page = Ordered(Page);

        Accounts = (await accounts.HandleAsync(null, null, false, cancellationToken))
            .Where(a => a.Kind == "Asset").ToArray();

        SpendCategories = await categories.HandleAsync("Expense", false, cancellationToken);
        IncomeCategories = await categories.HandleAsync("Income", false, cancellationToken);
        Rules = await listRules.HandleAsync(true, cancellationToken);
    }

    /// <summary>
    /// The month in the order the reader asked for. The whole month is already here - it is the
    /// page - so this is a sort of what is on screen rather than a second trip to the database,
    /// and it sorts the amounts as shown, after the sign convention has been applied to them.
    /// </summary>
    private TransactionPageDto Ordered(TransactionPageDto page)
    {
        var ascending = SortKey switch
        {
            "description" => page.Items.OrderBy(i => i.Description, StringComparer.CurrentCultureIgnoreCase),
            "category" => page.Items.OrderBy(i => i.CategoryName, StringComparer.CurrentCultureIgnoreCase),
            "account" => page.Items.OrderBy(i => i.AccountName, StringComparer.CurrentCultureIgnoreCase),
            "amount" => page.Items.OrderBy(i => i.Amount),
            _ => page.Items.OrderBy(i => i.OccurredOn)
        };

        // Reversing a stable ascending order is what makes the descending one a total order as
        // well: rows that tie keep a fixed position instead of shuffling between requests.
        var ordered = ascending.ThenBy(i => i.Id);

        return page with { Items = (SortDescending ? ordered.Reverse() : ordered).ToArray() };
    }
}

/// <summary>
/// The repeat panel's fields, bound under the <c>every</c> prefix so the ordinary entry fields
/// keep their own short names.
/// </summary>
public sealed class RepeatForm
{
    public string Frequency { get; set; } = "Monthly";
    public int Interval { get; set; } = 1;
    public DayOfWeek? DayOfWeek { get; set; }

    /// <summary>Monthly and yearly: which day number the rule lands on.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Monthly and yearly: push a Saturday or Sunday on to the following Monday.</summary>
    public bool MoveOffWeekends { get; set; }

    public int? Month { get; set; }
    public int Years { get; set; }
    public int Months { get; set; }
    public int Days { get; set; }
    public DateOnly? EndDate { get; set; }
}
