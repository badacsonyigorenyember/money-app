using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Import;
using Money.Application.Recurring;
using Money.Application.Transactions;
using Money.Domain.Time;

namespace Money.Api.Pages;

public sealed class TransactionsModel(
    ListTransactionsHandler list,
    QuickEntryHandler quickEntry,
    VoidTransactionHandler voidTransaction,
    GetCategoryTreeHandler categories,
    ListAccountsHandler accounts,
    ImportBankTransactionsHandler importBank,
    CreateRecurringRuleHandler createRule,
    ListRecurringRulesHandler listRules,
    UpdateRecurringRuleHandler updateRule,
    RecurringMaterialiser materialiser,
    ISettingsRepository settingsRepository,
    IClock clock) : PageModel
{
    public new TransactionPageDto Page { get; private set; } = new([], null);
    public IReadOnlyList<AccountDto> Accounts { get; private set; } = [];
    public IReadOnlyList<CategoryNodeDto> SpendCategories { get; private set; } = [];
    public IReadOnlyList<CategoryNodeDto> IncomeCategories { get; private set; } = [];
    public IReadOnlyList<RecurringRuleDto> Rules { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public string? ImportMessage { get; private set; }
    public string? RepeatMessage { get; private set; }
    public DateOnly Today { get; private set; }

    /// <summary>
    /// True when the repeating-rules list is being sent as an HTMX out-of-band update rather than
    /// rendered in its own place on the page. Adding a rule swaps the history table, and the
    /// rules list sits in a different section, so it rides along on the same response.
    /// </summary>
    public bool RulesOutOfBand { get; private set; }

    // FromQuery pins these to the query string. Without it they would also bind from a posted
    // form, and the quick-add form's own categoryId and accountId fields would silently become
    // filters - so recording one thing would hide everything else from the refreshed list.
    [BindProperty(SupportsGet = true), FromQuery] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true), FromQuery] public DateOnly? To { get; set; }
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

    public async Task<IActionResult> OnPostQuickAddAsync(
        [FromForm] decimal amount, [FromForm] Guid categoryId, [FromForm] Guid accountId,
        [FromForm] DateOnly? occurredOn, [FromForm] string? description,
        [FromForm] bool repeat, [FromForm] RepeatForm? every,
        CancellationToken cancellationToken)
    {
        if (repeat)
        {
            await AddRepeatingAsync(
                amount, categoryId, accountId, occurredOn, description,
                every ?? new RepeatForm(), cancellationToken);
        }
        else
        {
            var result = await quickEntry.HandleAsync(
                new QuickEntryRequest(amount, categoryId, accountId, occurredOn, description, null),
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

        var (dayOfMonth, weekOfMonth, dayOfWeekInMonth) = ParseMonthDay(every.MonthDay);

        var created = await createRule.HandleAsync(new CreateRecurringRuleRequest(
            direction, amount, accountId, categoryId, description, null,
            every.Frequency, every.Interval,
            (dayOfWeekInMonth ?? every.DayOfWeek)?.ToString(), weekOfMonth, dayOfMonth, every.Month,
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

    /// <summary>
    /// The single "which day" control offers a day number and a weekday-of-the-month in one list,
    /// so nothing else on the form has to change when the user switches between them. Values are
    /// <c>d:10</c> or <c>w:1:Monday</c>, with week -1 meaning the last one in the month.
    /// </summary>
    internal static (int? DayOfMonth, int? WeekOfMonth, DayOfWeek? DayOfWeek) ParseMonthDay(string? value)
    {
        var parts = (value ?? "").Split(':');

        if (parts is ["d", var day] && int.TryParse(day, CultureInfo.InvariantCulture, out var dayNumber))
            return (dayNumber, null, null);

        if (parts is ["w", var week, var weekday]
            && int.TryParse(week, CultureInfo.InvariantCulture, out var weekNumber)
            && Enum.TryParse<DayOfWeek>(weekday, out var parsedWeekday))
            return (null, weekNumber, parsedWeekday);

        return (null, null, null);
    }

    private static IEnumerable<CategoryNodeDto> Flatten(CategoryNodeDto node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));

    public async Task<IActionResult> OnPostImportBankAsync(
        [FromForm] Guid accountId, CancellationToken cancellationToken)
    {
        var result = await importBank.HandleAsync(
            new ImportBankTransactionsRequest(accountId, null), cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else ImportMessage = Describe(result.Value);

        return await RowsAsync(cancellationToken);
    }

    private static string Describe(ImportResultDto result)
    {
        var skipped = result.SkippedForeignCurrency > 0
            ? $", {result.SkippedForeignCurrency} skipped (other currency)"
            : "";

        return result.Imported == 0
            ? $"Nothing new between {result.From:yyyy-MM-dd} and {result.To:yyyy-MM-dd} " +
              $"({result.AlreadyPresent} already here{skipped})."
            : $"Added {result.Imported} from the bank, filed under Unclassified " +
              $"({result.AlreadyPresent} already here{skipped}).";
    }

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

        Page = await list.HandleAsync(
            new TransactionQuery(From, To, AccountId, CategoryId, Q, IncludeVoided, null, 50),
            cancellationToken);

        Accounts = (await accounts.HandleAsync(null, null, false, cancellationToken))
            .Where(a => a.Kind == "Asset").ToArray();

        SpendCategories = await categories.HandleAsync("Expense", false, cancellationToken);
        IncomeCategories = await categories.HandleAsync("Income", false, cancellationToken);
        Rules = await listRules.HandleAsync(true, cancellationToken);
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

    /// <summary>Monthly and yearly: <c>d:10</c> or <c>w:1:Monday</c>. See ParseMonthDay.</summary>
    public string? MonthDay { get; set; }

    public int? Month { get; set; }
    public int Years { get; set; }
    public int Months { get; set; }
    public int Days { get; set; }
    public DateOnly? EndDate { get; set; }
}
