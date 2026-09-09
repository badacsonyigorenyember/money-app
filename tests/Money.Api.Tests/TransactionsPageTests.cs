using System.Net;
using System.Text.RegularExpressions;

namespace Money.Api.Tests;

public sealed class TransactionsPageTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TransactionsPageTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_transactions_page_renders()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/transactions", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(CancellationToken.None);
        html.Should().Contain("Home");
        html.Should().Contain("Record something");
        html.Should().Contain("htmx.min.js");
    }

    [Fact]
    public async Task Changing_month_or_filters_keeps_the_reader_where_they_were()
    {
        using var client = _factory.CreateApiClient();

        var html = await client.GetStringAsync("/transactions", CancellationToken.None);

        // Both controls sit above a long list, so a plain navigation would throw the reader back
        // to the top of the page every time. Boosting them swaps <main> in place instead, and
        // show:none is what stops htmx from scrolling once the swap is done.
        foreach (var control in new[] { "month-nav", "toolbar" })
        {
            var tag = Regex.Match(html, @"<(?:nav|form)\b[^>]*\b" + control + @"\b[^>]*>").Value;

            tag.Should().Contain("hx-boost", "the {0} must not reload the page", control);
            tag.Should().Contain("show:none", "the {0} must not scroll after swapping", control);
        }
    }

    [Fact]
    public async Task An_empty_month_says_which_month_is_empty()
    {
        using var client = _factory.CreateApiClient();

        var html = await client.GetStringAsync("/transactions?month=2020-M01", CancellationToken.None);

        // Stepping back through the months reaches this as often as a first run does, so it
        // names the month rather than assuming the reader has never recorded anything.
        html.Should().Contain("Nothing in January 2020");
        html.Should().NotContain("Record your first spend");
    }

    [Fact]
    public async Task No_page_uses_accounting_vocabulary()
    {
        using var client = _factory.CreateApiClient();

        // Every page that exists goes in here: transactions, categories, accounts, settings and
        // the first-run wizard. This is the automated guard for the rule that double-entry
        // vocabulary never reaches a view - widen it again the moment a new page is added.
        foreach (var path in new[] { "/transactions", "/categories", "/accounts", "/settings", "/FirstRun" })
        {
            var html = await client.GetStringAsync(path, CancellationToken.None);

            html.Should().NotContainEquivalentOf("posting", "the UI never says 'posting' ({0})", path);
            html.Should().NotContainEquivalentOf("double-entry", "({0})", path);
            html.Should().NotContainEquivalentOf(">debit<", "({0})", path);
            html.Should().NotContainEquivalentOf(">credit<", "({0})", path);
        }
    }

    [Fact]
    public async Task A_recorded_expense_shows_up_in_the_rendered_list()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await _factory.CreateAccountAsync(
            "Bank " + suffix, "Bank", openingBalance: 500m, openedOn: new DateOnly(2026, 1, 1));
        var category = await _factory.CreateCategoryAsync("Books " + suffix, "Expense");

        await _factory.QuickEntryAsync(
            19.99m, category.Id, bank.Id, new DateOnly(2026, 9, 1), "Novel " + suffix);

        var html = await client.GetStringAsync("/transactions", CancellationToken.None);

        html.Should().Contain("Novel " + suffix);
        html.Should().Contain("19.99");
    }

    [Fact]
    public async Task The_page_is_responsive()
    {
        using var client = _factory.CreateApiClient();

        var html = await client.GetStringAsync("/transactions", CancellationToken.None);

        html.Should().Contain("name=\"viewport\"");
    }

    [Fact]
    public async Task Asking_for_confirmation_does_not_depend_on_window_confirm()
    {
        using var client = _factory.CreateApiClient();

        // hx-confirm goes through window.confirm, and a host can suppress that dialog - the
        // Remove button then swallowed the click and did nothing at all. Every page carries the
        // layout, so every hx-confirm anywhere in the app routes through this one <dialog>.
        var html = await client.GetStringAsync("/transactions", CancellationToken.None);

        html.Should().Contain("htmx:confirm", "the layout must intercept htmx's confirmation step");
        html.Should().Contain("id=\"confirm\"", "and open its own dialog instead");
    }

    [Fact]
    public async Task Voiding_without_an_antiforgery_token_is_rejected()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var transactionId = await CreateExpenseAsync(suffix);

        // No prior GET, so this client carries neither the antiforgery cookie nor the header
        // token that Shared/_Layout.cshtml stamps onto <body> via hx-headers. This is the
        // Remove button's path: it posts from outside any <form>, so it has no hidden
        // "__RequestVerificationToken" field to fall back on either.
        var response = await client.PostAsync(
            $"/transactions?handler=Void&id={transactionId}&reason=Removed+by+the+user",
            content: null, CancellationToken.None);

        // Pinned to what this app actually returns: Razor Pages' built-in antiforgery
        // validation fails inside an authorization filter, which MoneyWebApp's
        // app.UseExceptionHandler() + AddProblemDetails() turns into a 400 Problem Details
        // response (AntiforgeryValidationException is treated as a bad request, not a 500).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Voiding_with_a_valid_antiforgery_token_succeeds()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var transactionId = await CreateExpenseAsync(suffix);

        // Fetch the page first, exactly like a browser would: this both sets the antiforgery
        // cookie (WebApplicationFactoryClientOptions.HandleCookies defaults to true, so the
        // same HttpClient replays it automatically) and lets us read the header token that
        // _Layout.cshtml stamps into hx-headers on <body>.
        var pageHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        var token = Regex.Match(pageHtml, "RequestVerificationToken\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        token.Should().NotBeNullOrEmpty("the layout should always stamp a token onto <body>");

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/transactions?handler=Void&id={transactionId}&reason=Removed+by+the+user");
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request, CancellationToken.None);

        // Proves the 400 above is really about the missing token, not some other defect in the
        // request: the same request, only now carrying a valid token, succeeds and actually
        // voids the transaction (read back through the use case, since the returned rows
        // partial excludes voided items by default and so would look identical whether or not
        // the void call actually did anything).
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var withVoided = await _factory.ListTransactionsAsync(includeVoided: true);
        withVoided.Items.Should().Contain(item => item.Id == transactionId && item.IsVoided);
    }

    private async Task<Guid> CreateExpenseAsync(string suffix)
    {
        var bank = await _factory.CreateAccountAsync(
            "Bank " + suffix, "Bank", openingBalance: 500m, openedOn: new DateOnly(2026, 1, 1));
        var category = await _factory.CreateCategoryAsync("Books " + suffix, "Expense");

        var transaction = await _factory.QuickEntryAsync(
            19.99m, category.Id, bank.Id, new DateOnly(2026, 9, 1), "Novel " + suffix);

        return transaction.Id;
    }

    [Fact]
    public async Task The_repeat_panel_offers_a_day_of_the_month_and_a_weekday_of_the_month()
    {
        using var client = _factory.CreateApiClient();

        var html = await client.GetStringAsync("/transactions", CancellationToken.None);

        html.Should().Contain("Repeat this automatically");
        html.Should().Contain("value=\"d:10\"", "the 10th of the month must be pickable");
        html.Should().Contain("value=\"w:1:Monday\"", "the first Monday must be pickable");
        html.Should().Contain("value=\"w:-1:Friday\"", "the last Friday must be pickable");
        html.Should().Contain("every.Years", "a custom repeat needs its own years/months/days");
    }

    /// <summary>
    /// The whole point of the feature, end to end through the screen the user actually uses:
    /// tick the box, get a rule, and get the entries it already owes.
    /// </summary>
    [Fact]
    public async Task Ticking_repeat_creates_a_rule_and_posts_what_it_already_owes()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var description = "Salary " + suffix;

        var bank = await _factory.CreateAccountAsync(
            "Bank " + suffix, "Bank", openingBalance: 500m, openedOn: new DateOnly(2026, 1, 1));
        var category = await _factory.CreateCategoryAsync("Pay " + suffix, "Income");

        var pageHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        var token = Regex.Match(pageHtml, @"RequestVerificationToken""\s*:\s*""([^""]+)""").Groups[1].Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions?handler=QuickAdd");
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = "2500",
            ["categoryId"] = category.Id.ToString(),
            ["accountId"] = bank.Id.ToString(),
            ["occurredOn"] = "2026-07-01",
            ["description"] = description,
            ["repeat"] = "true",
            ["every.Frequency"] = "Monthly",
            ["every.Interval"] = "1",
            ["every.MonthDay"] = "d:1"
        });

        var response = await client.SendAsync(request, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await response.Content.ReadAsStringAsync(CancellationToken.None);
        rows.Should().Contain("now repeats");
        rows.Should().Contain("hx-swap-oob", "the repeating list is refreshed on the same response");

        // The factory's clock reads 1 September 2026, so July, August and September are due.
        var listed = await _factory.ListTransactionsAsync(description);

        listed.Items.Should().HaveCount(3);
        listed.Items.Should().OnlyContain(item => item.Amount == 2500m);
    }

    /// <summary>
    /// The rows that come back after recording something are the history, not a view filtered
    /// by whatever was just entered: the form posts a category and an account, and those must
    /// not be read as the page's category and account filters.
    /// </summary>
    [Fact]
    public async Task Recording_something_leaves_the_rest_of_the_history_in_place()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await _factory.CreateAccountAsync(
            "Bank " + suffix, "Bank", openingBalance: 500m, openedOn: new DateOnly(2026, 1, 1));
        var books = await _factory.CreateCategoryAsync("Books " + suffix, "Expense");
        var fuel = await _factory.CreateCategoryAsync("Fuel " + suffix, "Expense");

        await _factory.QuickEntryAsync(
            19.99m, books.Id, bank.Id, new DateOnly(2026, 9, 1), "Novel " + suffix);

        var pageHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        var token = Regex.Match(pageHtml, @"RequestVerificationToken""\s*:\s*""([^""]+)""").Groups[1].Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions?handler=QuickAdd");
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = "40.00",
            ["categoryId"] = fuel.Id.ToString(),
            ["accountId"] = bank.Id.ToString(),
            ["occurredOn"] = "2026-09-01",
            ["description"] = "Diesel " + suffix
        });

        var rows = await (await client.SendAsync(request, CancellationToken.None))
            .Content.ReadAsStringAsync(CancellationToken.None);

        rows.Should().Contain("Diesel " + suffix);
        rows.Should().Contain("Novel " + suffix,
            "the earlier entry is still history, whatever category was just used");
    }

    /// <summary>
    /// The page shows one month at a time, and the arrows walk it. The month in the query string
    /// has to reach the entry list as well as the chart, or paging back would redraw the chart
    /// over a list that never moved.
    /// </summary>
    [Fact]
    public async Task Paging_to_another_month_changes_which_entries_are_listed()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await _factory.CreateAccountAsync("Bank " + suffix, "Bank");
        var food = await _factory.CreateCategoryAsync("Food " + suffix, "Expense");

        foreach (var (day, what) in new[]
                 { (new DateOnly(2026, 7, 14), "Melon"), (new DateOnly(2026, 9, 1), "Bread") })
        {
            await _factory.QuickEntryAsync(5m, food.Id, bank.Id, day, what + " " + suffix);
        }

        var thisMonth = await client.GetStringAsync("/transactions", CancellationToken.None);
        thisMonth.Should().Contain("Bread " + suffix);
        thisMonth.Should().NotContain("Melon " + suffix);

        var july = await client.GetStringAsync("/transactions?month=2026-M07", CancellationToken.None);
        july.Should().Contain("Melon " + suffix);
        july.Should().NotContain("Bread " + suffix);
    }

    /// <summary>
    /// The month is the page - every row is already on it - so ordering it is the reader's
    /// choice of column, not another query. The heading is the control, and it is a plain link,
    /// so the order lives in the address and the back button walks it.
    /// </summary>
    [Fact]
    public async Task Any_column_can_order_the_month()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await _factory.CreateAccountAsync(
            "Bank " + suffix, "Bank", openingBalance: 900m, openedOn: new DateOnly(2026, 1, 1));
        var books = await _factory.CreateCategoryAsync("Books " + suffix, "Expense");

        // Deliberately not in amount order by date, so ordering by amount has to actually move
        // the rows rather than leave them where the default put them.
        await _factory.QuickEntryAsync(30m, books.Id, bank.Id, new DateOnly(2026, 9, 2), "Middling " + suffix);
        await _factory.QuickEntryAsync(10m, books.Id, bank.Id, new DateOnly(2026, 9, 3), "Cheapest " + suffix);
        await _factory.QuickEntryAsync(50m, books.Id, bank.Id, new DateOnly(2026, 9, 1), "Dearest " + suffix);

        var ascending = await client.GetStringAsync(
            "/transactions?month=2026-M09&sort=amount", CancellationToken.None);

        Position(ascending, "Cheapest " + suffix).Should().BeLessThan(Position(ascending, "Middling " + suffix));
        Position(ascending, "Middling " + suffix).Should().BeLessThan(Position(ascending, "Dearest " + suffix));

        ascending.Should().Contain("aria-sort=\"ascending\"", "the ordered column has to say so");

        var descending = await client.GetStringAsync(
            "/transactions?month=2026-M09&sort=-amount", CancellationToken.None);

        Position(descending, "Dearest " + suffix).Should().BeLessThan(Position(descending, "Cheapest " + suffix));

        // The default is the one the list has always had: newest first, and it offers every
        // other column as a link rather than needing the query string typed by hand.
        var byDate = await client.GetStringAsync("/transactions?month=2026-M09", CancellationToken.None);

        Position(byDate, "Cheapest " + suffix).Should().BeLessThan(Position(byDate, "Dearest " + suffix));
        byDate.Should().Contain("sort=amount");
        byDate.Should().Contain("sort=description");
    }

    /// <summary>
    /// Removing a row redraws the list from the server, and the order the reader chose is not
    /// part of that form - so it has to ride on the request, or a removal silently re-sorts the
    /// month back to newest-first under them.
    /// </summary>
    [Fact]
    public async Task The_chosen_order_survives_removing_a_row()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await _factory.CreateAccountAsync(
            "Bank " + suffix, "Bank", openingBalance: 900m, openedOn: new DateOnly(2026, 1, 1));
        var books = await _factory.CreateCategoryAsync("Books " + suffix, "Expense");

        await _factory.QuickEntryAsync(30m, books.Id, bank.Id, new DateOnly(2026, 9, 2), "Keeper " + suffix);
        var doomed = await _factory.QuickEntryAsync(
            10m, books.Id, bank.Id, new DateOnly(2026, 9, 3), "Doomed " + suffix);

        var page = await client.GetStringAsync(
            "/transactions?month=2026-M09&sort=-amount", CancellationToken.None);
        var token = Regex.Match(page, @"RequestVerificationToken""\s*:\s*""([^""]+)""").Groups[1].Value;

        var remove = Regex.Match(page, @"/transactions\?handler=Void&amp;[^""]*id=" + doomed.Id + @"[^""]*").Value;
        remove.Should().NotBeEmpty("the Remove button posts to a URL carrying the row's id");
        remove.Should().Contain("sort=-amount", "and the order the reader is looking at");

        using var request = new HttpRequestMessage(HttpMethod.Post, remove.Replace("&amp;", "&", StringComparison.Ordinal));
        request.Headers.Add("RequestVerificationToken", token);

        var rows = await (await client.SendAsync(request, CancellationToken.None))
            .Content.ReadAsStringAsync(CancellationToken.None);

        rows.Should().Contain("aria-sort=\"descending\"");
        rows.Should().Contain("sort=-amount", "the rows that come back still offer the same order");
    }

    private static int Position(string html, string description)
    {
        var at = html.IndexOf(description, StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, "{0} should be listed", description);
        return at;
    }

    /// <summary>
    /// A line floating in the middle of the box says how the month went but not how much money
    /// there is. Zero stays on the axis whatever the balances are, so the height of the line is
    /// readable as an amount rather than only as a shape.
    /// </summary>
    [Fact]
    public async Task The_chart_keeps_zero_on_the_axis_however_far_the_balance_is_from_it()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await _factory.CreateAccountAsync(
            "Vault " + suffix, "Bank", openingBalance: 4200m, openedOn: new DateOnly(2026, 1, 1));

        var chart = await client.GetStringAsync(
            $"/transactions?handler=Chart&picked=true&selected={bank.Id}", CancellationToken.None);

        chart.Should().Contain("grid--zero",
            "a chart of a 4200 balance still has to show where zero is");
    }

    /// <summary>
    /// "Move money" is a choice in the same category picker, not a screen of its own: it swaps the
    /// one account for a pair, and the entry it writes is a transfer, so nothing leaves the ledger
    /// and no expense category is touched.
    /// </summary>
    [Fact]
    public async Task Choosing_move_money_transfers_between_the_two_chosen_accounts()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var current = await _factory.CreateAccountAsync(
            "Current " + suffix, "Bank", openingBalance: 1000m, openedOn: new DateOnly(2026, 1, 1));
        var savings = await _factory.CreateAccountAsync(
            "Savings " + suffix, "Bank", openingBalance: 0m, openedOn: new DateOnly(2026, 1, 1));

        var pageHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        pageHtml.Should().Contain("Move money", "the picker offers it once there are two accounts");

        var token = Regex.Match(pageHtml, @"RequestVerificationToken""\s*:\s*""([^""]+)""").Groups[1].Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions?handler=QuickAdd");
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = "250",
            ["categoryId"] = "move",
            ["accountId"] = current.Id.ToString(),
            ["fromAccountId"] = current.Id.ToString(),
            ["toAccountId"] = savings.Id.ToString(),
            ["occurredOn"] = "2026-09-01",
            ["description"] = "Sweep " + suffix
        });

        var response = await client.SendAsync(request, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await response.Content.ReadAsStringAsync(CancellationToken.None);
        rows.Should().Contain("Sweep " + suffix);

        (await _factory.BalanceAsync(current.Id)).Should().Be(750m);
        (await _factory.BalanceAsync(savings.Id)).Should().Be(250m);
    }

    /// <summary>
    /// The same account on both sides is a mistake, not a no-op, and it is said on the page rather
    /// than thrown.
    /// </summary>
    [Fact]
    public async Task Moving_money_to_the_same_account_records_nothing()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var current = await _factory.CreateAccountAsync(
            "Only " + suffix, "Bank", openingBalance: 100m, openedOn: new DateOnly(2026, 1, 1));

        var pageHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        var token = Regex.Match(pageHtml, @"RequestVerificationToken""\s*:\s*""([^""]+)""").Groups[1].Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions?handler=QuickAdd");
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = "10",
            ["categoryId"] = "move",
            ["accountId"] = current.Id.ToString(),
            ["fromAccountId"] = current.Id.ToString(),
            ["toAccountId"] = current.Id.ToString(),
            ["occurredOn"] = "2026-09-01",
            ["description"] = "Nowhere " + suffix
        });

        var rows = await (await client.SendAsync(request, CancellationToken.None))
            .Content.ReadAsStringAsync(CancellationToken.None);

        rows.Should().NotContain("Nowhere " + suffix);
        (await _factory.BalanceAsync(current.Id)).Should().Be(100m);
    }
}
