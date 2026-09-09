using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Money.Application.Accounts;
using Money.Application.Admin;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;

namespace Money.Api.Tests;

/// <summary>
/// Spec section 13, phase 2: "A usable manual expense tracker. Round-trip: add expense, see it in
/// the list, see the balance change." This test is that sentence.
///
/// The app has no JSON API - the desktop window is the only client, and it talks to Razor page
/// handlers - so setting the ledger up and reading it back both go through the same use cases a
/// page would call. What is asserted over HTTP is what the window actually sees: rendered pages.
/// </summary>
public sealed class RoundTripAcceptanceTests
{
    [Fact]
    public async Task A_new_user_can_set_up_record_an_expense_and_see_it_everywhere()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        // 1. First run: currency, payday anchor, first account, starter categories.
        //
        // A real browser GETs the wizard before submitting its <form>, which is what sets the
        // antiforgery cookie and stamps the header token onto <body> (Shared/_Layout.cshtml) that
        // this request replays - the same mechanism FirstRunPageTests.
        // Completing_the_wizard_seeds_the_app_and_stops_redirecting already pins in both
        // directions. Posting without it does not exercise a product defect, it just fails
        // Razor Pages' built-in antiforgery filter, so the fetch-then-post dance belongs in the
        // test, not in the product.
        await CompleteFirstRunAsync(client, new Dictionary<string, string>
        {
            ["BaseCurrencyCode"] = "EUR",
            ["PeriodAnchor"] = "DayOfMonth",
            ["PeriodAnchorDay"] = "25",
            ["TimeZoneId"] = "Europe/Budapest",
            ["FirstDayOfWeek"] = "Monday",
            ["FirstAccountName"] = "Current account",
            ["FirstAccountRole"] = "Bank",
            ["OpeningBalance"] = "1500.00",
            ["OpenedOn"] = "2026-01-01",
            ["SeedStarterCategories"] = "true"
        });

        var accounts = await factory.UseAsync(services =>
            services.GetRequiredService<ListAccountsHandler>().HandleAsync("Asset", null, false));
        var bank = accounts.Single(a => a.Name == "Current account");

        var categories = await factory.UseAsync(services =>
            services.GetRequiredService<GetCategoryTreeHandler>().HandleAsync("Expense", false));
        var groceries = Flatten(categories).Single(c => c.Name == "Groceries");

        // 2. Record an expense.
        await factory.QuickEntryAsync(
            42.35m, groceries.Id, bank.Id, new DateOnly(2026, 9, 1), "Weekly shop");

        // 3. See it in the list - on the page, which is the only place a user ever sees it.
        var html = await client.GetStringAsync("/transactions", CancellationToken.None);
        html.Should().Contain("Weekly shop").And.Contain("42.35");

        // 4. See the balance change.
        (await factory.BalanceAsync(bank.Id)).Should().Be(1457.65m);

        // 5. Move money to savings, and confirm it is not spending.
        var savings = await factory.CreateAccountAsync("Rainy day", "SavingsPocket");

        var transfer = await factory.UseAsync(services =>
            services.GetRequiredService<TransferHandler>().HandleAsync(
                new TransferRequest(300m, bank.Id, savings.Id, new DateOnly(2026, 9, 2), null)));
        transfer.IsSuccess.Should().BeTrue(transfer.Error?.Message);

        (await factory.BalanceAsync(bank.Id)).Should().Be(1157.65m,
            "the transfer must actually debit the source account");
        (await factory.BalanceAsync(savings.Id)).Should().Be(300m,
            "the transfer must actually credit the destination account");
        (await factory.BalanceAsync(groceries.Id)).Should().Be(42.35m,
            "moving money to savings is not spending (I12)");

        // 6. The books still balance, and the data passes its own integrity check.
        var report = await factory.UseAsync(services =>
            services.GetRequiredService<RunIntegrityCheckHandler>().HandleAsync());
        report.IsHealthy.Should().BeTrue();

        // 7. Everything can be exported - from the two buttons on the Settings page.
        var export = await client.GetAsync("/settings?handler=Export&format=json", CancellationToken.None);
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        (await export.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("Weekly shop");
    }

    [Fact]
    public async Task A_JPY_ledgers_amounts_render_without_invented_decimals()
    {
        // I3: _TransactionRows.cshtml and _AccountRows.cshtml hardcoded ToString("N2"), so a
        // zero-decimal currency such as JPY (MinorUnitExponent 0) rendered with two decimal
        // places it never actually had. This also exercises C1: the account and category below
        // are created with no currency specified, so they only end up JPY because first run set
        // the ledger's base currency to JPY.
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        await CompleteFirstRunAsync(client, new Dictionary<string, string>
        {
            ["BaseCurrencyCode"] = "JPY",
            ["PeriodAnchor"] = "DayOfMonth",
            ["PeriodAnchorDay"] = "25",
            ["TimeZoneId"] = "Europe/Budapest",
            ["FirstDayOfWeek"] = "Monday",
            ["FirstAccountName"] = "Wallet",
            ["FirstAccountRole"] = "Cash",
            ["OpeningBalance"] = "10000",
            ["OpenedOn"] = "2026-01-01",
            ["SeedStarterCategories"] = "false"
        });

        var accounts = await factory.UseAsync(services =>
            services.GetRequiredService<ListAccountsHandler>().HandleAsync("Asset", null, false));
        var bank = accounts.Single(a => a.Name == "Wallet");
        bank.CurrencyCode.Should().Be("JPY");

        var category = await factory.CreateCategoryAsync("Ramen", "Expense");
        category.CurrencyCode.Should().Be("JPY");

        await factory.QuickEntryAsync(
            1235m, category.Id, bank.Id, new DateOnly(2026, 9, 1), "Ramen shop");

        // The amount and its currency code are separate elements now, so these look for the
        // rendered figure and the code independently rather than for one run of text.
        var transactionsHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        transactionsHtml.Should().Contain("1,235");
        transactionsHtml.Should().NotContain("1,235.00");
        transactionsHtml.Should().Contain("JPY");

        var accountsHtml = await client.GetStringAsync("/accounts", CancellationToken.None);
        accountsHtml.Should().Contain("8,765");
        accountsHtml.Should().NotContain("8,765.00");
        accountsHtml.Should().Contain("JPY");
    }

    [Fact]
    public async Task The_accounts_pages_own_form_also_inherits_the_base_currency_not_a_hardcoded_EUR()
    {
        // C1: Accounts.cshtml.cs's OnPostCreateAsync used to pass the literal "EUR" to
        // CreateAccountRequest regardless of the ledger's base currency. This drives that exact
        // form handler to make sure the page-level call site is fixed too.
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        await CompleteFirstRunAsync(client, new Dictionary<string, string>
        {
            ["BaseCurrencyCode"] = "HUF",
            ["PeriodAnchor"] = "DayOfMonth",
            ["PeriodAnchorDay"] = "25",
            ["TimeZoneId"] = "Europe/Budapest",
            ["FirstDayOfWeek"] = "Monday",
            ["FirstAccountName"] = "Current account",
            ["FirstAccountRole"] = "Bank",
            ["OpeningBalance"] = "0",
            ["OpenedOn"] = "2026-01-01",
            ["SeedStarterCategories"] = "false"
        });

        var accountsHtml = await client.GetStringAsync("/accounts", CancellationToken.None);
        var accountsToken = TokenIn(accountsHtml);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/accounts?handler=Create")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["name"] = "Savings",
                ["role"] = "SavingsPocket"
            })
        };
        createRequest.Headers.Add("RequestVerificationToken", accountsToken);

        var created = await client.SendAsync(createRequest, CancellationToken.None);
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        var accounts = await factory.UseAsync(services =>
            services.GetRequiredService<ListAccountsHandler>().HandleAsync("Asset", null, false));
        accounts.Single(a => a.Name == "Savings").CurrencyCode.Should().Be("HUF");
    }

    [Fact]
    public async Task A_mistaken_entry_can_be_removed_and_the_balance_returns()
    {
        using var factory = new ApiFactory();

        var bank = await factory.CreateAccountAsync(
            "Wallet", "Cash", openingBalance: 100m, openedOn: new DateOnly(2026, 1, 1));
        var category = await factory.CreateCategoryAsync("Snacks", "Expense");

        var created = await factory.QuickEntryAsync(
            9.99m, category.Id, bank.Id, new DateOnly(2026, 9, 1), "Oops");

        var voided = await factory.UseAsync(services =>
            services.GetRequiredService<VoidTransactionHandler>()
                    .HandleAsync(created.Id, "Entered twice"));
        voided.IsSuccess.Should().BeTrue(voided.Error?.Message);

        (await factory.BalanceAsync(bank.Id)).Should().Be(100m);

        var withVoided = await factory.ListTransactionsAsync(includeVoided: true);
        withVoided.Items.Should().Contain(i => i.Id == created.Id,
            "history is kept; nothing is ever deleted (spec D11)");
    }

    private static async Task CompleteFirstRunAsync(HttpClient client, Dictionary<string, string> fields)
    {
        var wizardHtml = await client.GetStringAsync("/FirstRun", CancellationToken.None);
        var token = TokenIn(wizardHtml);
        token.Should().NotBeNullOrEmpty("the layout should always stamp a token onto <body>");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/FirstRun")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Add("RequestVerificationToken", token);

        (await client.SendAsync(request, CancellationToken.None))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    private static string TokenIn(string html) =>
        Regex.Match(html, "RequestVerificationToken\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;

    private static IEnumerable<CategoryNodeDto> Flatten(IEnumerable<CategoryNodeDto> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
}
