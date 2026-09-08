using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Money.Application.Contracts;

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

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 500m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Books " + suffix, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        await client.PostAsJsonAsync("/api/v1/transactions/quick-entry",
            new { amount = 19.99m, categoryId = category!.Id, accountId = bank!.Id,
                  occurredOn = "2026-09-01", description = "Novel " + suffix },
            CancellationToken.None);

        var html = await client.GetStringAsync("/transactions", CancellationToken.None);

        html.Should().Contain("Novel " + suffix);
        html.Should().Contain("19.99");
    }

    [Fact]
    public async Task The_page_is_responsive_and_declares_a_pwa_manifest()
    {
        using var client = _factory.CreateApiClient();

        var html = await client.GetStringAsync("/transactions", CancellationToken.None);

        html.Should().Contain("name=\"viewport\"");
        html.Should().Contain("manifest.webmanifest");
    }

    [Fact]
    public async Task Voiding_without_an_antiforgery_token_is_rejected()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var transactionId = await CreateExpenseAsync(client, suffix);

        // No prior GET, so this client carries neither the antiforgery cookie nor the header
        // token that Shared/_Layout.cshtml stamps onto <body> via hx-headers. This is the
        // Remove button's path: it posts from outside any <form>, so it has no hidden
        // "__RequestVerificationToken" field to fall back on either.
        var response = await client.PostAsync(
            $"/transactions?handler=Void&id={transactionId}&reason=Removed+by+the+user",
            content: null, CancellationToken.None);

        // Pinned to what this app actually returns: Razor Pages' built-in antiforgery
        // validation fails inside an authorization filter, which Program.cs's
        // app.UseExceptionHandler() + AddProblemDetails() turns into a 400 Problem Details
        // response (AntiforgeryValidationException is treated as a bad request, not a 500).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Voiding_with_a_valid_antiforgery_token_succeeds()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var transactionId = await CreateExpenseAsync(client, suffix);

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

        // Proves the 400 above is really about the missing token, not some other defect in
        // the request: the same request, only now carrying a valid token, succeeds and
        // actually voids the transaction (checked independently via the JSON API, since the
        // returned rows partial excludes voided items by default and so would look identical
        // whether or not the void call actually did anything).
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var voided = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/v1/transactions/{transactionId}", CancellationToken.None);
        voided!.IsVoided.Should().BeTrue();
    }

    private static async Task<Guid> CreateExpenseAsync(HttpClient client, string suffix)
    {
        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 500m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Books " + suffix, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var transaction = await (await client.PostAsJsonAsync("/api/v1/transactions/quick-entry",
            new { amount = 19.99m, categoryId = category!.Id, accountId = bank!.Id,
                  occurredOn = "2026-09-01", description = "Novel " + suffix },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<TransactionDto>(CancellationToken.None);

        return transaction!.Id;
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

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 500m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Pay " + suffix, kind = "Income" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var pageHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        var token = Regex.Match(pageHtml, @"RequestVerificationToken""\s*:\s*""([^""]+)""").Groups[1].Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions?handler=QuickAdd");
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = "2500",
            ["categoryId"] = category!.Id.ToString(),
            ["accountId"] = bank!.Id.ToString(),
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
        var listed = await client.GetFromJsonAsync<TransactionPageDto>(
            $"/api/v1/transactions?q={description}", CancellationToken.None);

        listed!.Items.Should().HaveCount(3);
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

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 500m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var books = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Books " + suffix, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var fuel = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Fuel " + suffix, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        await client.PostAsJsonAsync("/api/v1/transactions/quick-entry",
            new { amount = 19.99m, categoryId = books!.Id, accountId = bank!.Id,
                  occurredOn = "2026-08-01", description = "Novel " + suffix },
            CancellationToken.None);

        var pageHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        var token = Regex.Match(pageHtml, @"RequestVerificationToken""\s*:\s*""([^""]+)""").Groups[1].Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions?handler=QuickAdd");
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = "40.00",
            ["categoryId"] = fuel!.Id.ToString(),
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
}
