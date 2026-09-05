using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Money.Application.Contracts;

namespace Money.Api.Tests;

/// <summary>
/// Spec section 13, phase 2: "A usable manual expense tracker. Round-trip: add expense, see it in
/// the list, see the balance change." This test is that sentence.
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
        var wizardHtml = await client.GetStringAsync("/FirstRun", CancellationToken.None);
        var token = Regex.Match(wizardHtml, "RequestVerificationToken\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        token.Should().NotBeNullOrEmpty("the layout should always stamp a token onto <body>");

        var wizard = new FormUrlEncodedContent(new Dictionary<string, string>
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

        using var wizardRequest = new HttpRequestMessage(HttpMethod.Post, "/FirstRun") { Content = wizard };
        wizardRequest.Headers.Add("RequestVerificationToken", token);

        (await client.SendAsync(wizardRequest, CancellationToken.None))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        var accounts = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts?kind=Asset", CancellationToken.None);
        var bank = accounts!.Single(a => a.Name == "Current account");

        var categories = await client.GetFromJsonAsync<List<CategoryNodeDto>>(
            "/api/v1/categories?kind=Expense", CancellationToken.None);
        var groceries = categories!.Single(c => c.Name == "Groceries");

        // 2. Record an expense.
        var expense = await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 42.35m, categoryId = groceries.Id, accountId = bank.Id,
                  occurredOn = "2026-09-01", description = "Weekly shop" },
            CancellationToken.None);
        expense.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. See it in the list.
        var page = await client.GetFromJsonAsync<TransactionPageDto>(
            "/api/v1/transactions", CancellationToken.None);
        page!.Items.Should().Contain(i => i.Description == "Weekly shop" && i.Amount == 42.35m);

        var html = await client.GetStringAsync("/transactions", CancellationToken.None);
        html.Should().Contain("Weekly shop").And.Contain("42.35");

        // 4. See the balance change.
        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank.Id}/balance", CancellationToken.None);
        balance!.Balance.Should().Be(1457.65m);

        // 5. Move money to savings, and confirm it is not spending.
        var savings = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Rainy day", kind = "Asset", role = "SavingsPocket", currencyCode = "EUR" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var bankBeforeTransfer = balance.Balance;

        var transfer = await client.PostAsJsonAsync("/api/v1/transactions/transfer",
            new { amount = 300m, fromAccountId = bank.Id, toAccountId = savings!.Id,
                  occurredOn = "2026-09-02" }, CancellationToken.None);
        transfer.StatusCode.Should().Be(HttpStatusCode.Created);

        var bankAfterTransfer = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank.Id}/balance", CancellationToken.None);
        bankAfterTransfer!.Balance.Should().Be(bankBeforeTransfer - 300m,
            "the transfer must actually debit the source account");

        var savingsAfterTransfer = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{savings.Id}/balance", CancellationToken.None);
        savingsAfterTransfer!.Balance.Should().Be(300m,
            "the transfer must actually credit the destination account");

        var groceriesBalance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{groceries.Id}/balance", CancellationToken.None);
        groceriesBalance!.Balance.Should().Be(42.35m,
            "moving money to savings is not spending (I12)");

        // 6. The books still balance, and the data passes its own integrity check.
        var report = await (await client.PostAsync("/api/v1/admin/integrity-check", null,
                                                   CancellationToken.None))
            .Content.ReadFromJsonAsync<IntegrityReportDto>(CancellationToken.None);
        report!.IsHealthy.Should().BeTrue();

        // 7. Everything can be exported.
        var export = await client.GetAsync("/api/v1/admin/export?format=json",
                                           CancellationToken.None);
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

        var wizardHtml = await client.GetStringAsync("/FirstRun", CancellationToken.None);
        var token = Regex.Match(wizardHtml, "RequestVerificationToken\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;

        var wizard = new FormUrlEncodedContent(new Dictionary<string, string>
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

        using var wizardRequest = new HttpRequestMessage(HttpMethod.Post, "/FirstRun") { Content = wizard };
        wizardRequest.Headers.Add("RequestVerificationToken", token);
        (await client.SendAsync(wizardRequest, CancellationToken.None))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        var bank = (await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts?kind=Asset", CancellationToken.None))!.Single(a => a.Name == "Wallet");
        bank.CurrencyCode.Should().Be("JPY");

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Ramen", kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);
        category!.CurrencyCode.Should().Be("JPY");

        await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 1235m, categoryId = category.Id, accountId = bank.Id,
                  occurredOn = "2026-09-01", description = "Ramen shop" },
            CancellationToken.None);

        var transactionsHtml = await client.GetStringAsync("/transactions", CancellationToken.None);
        transactionsHtml.Should().Contain("1,235 JPY");
        transactionsHtml.Should().NotContain("1,235.00");

        var accountsHtml = await client.GetStringAsync("/accounts", CancellationToken.None);
        accountsHtml.Should().Contain("8,765 JPY");
        accountsHtml.Should().NotContain("8,765.00");
    }

    [Fact]
    public async Task The_accounts_pages_own_form_also_inherits_the_base_currency_not_a_hardcoded_EUR()
    {
        // C1: Accounts.cshtml.cs's OnPostCreateAsync used to pass the literal "EUR" to
        // CreateAccountRequest regardless of the ledger's base currency. This drives that exact
        // form handler (not the JSON API) to make sure the page-level call site is fixed too.
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var wizardHtml = await client.GetStringAsync("/FirstRun", CancellationToken.None);
        var wizardToken = Regex.Match(wizardHtml, "RequestVerificationToken\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;

        var wizard = new FormUrlEncodedContent(new Dictionary<string, string>
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

        using var wizardRequest = new HttpRequestMessage(HttpMethod.Post, "/FirstRun") { Content = wizard };
        wizardRequest.Headers.Add("RequestVerificationToken", wizardToken);
        (await client.SendAsync(wizardRequest, CancellationToken.None))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        var accountsHtml = await client.GetStringAsync("/accounts", CancellationToken.None);
        var accountsToken = Regex.Match(accountsHtml, "RequestVerificationToken\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "Savings",
            ["role"] = "SavingsPocket"
        });
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/accounts?handler=Create")
        {
            Content = form
        };
        createRequest.Headers.Add("RequestVerificationToken", accountsToken);

        var created = await client.SendAsync(createRequest, CancellationToken.None);
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        var accounts = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts?kind=Asset", CancellationToken.None);
        accounts!.Single(a => a.Name == "Savings").CurrencyCode.Should().Be("HUF");
    }

    [Fact]
    public async Task A_mistaken_entry_can_be_removed_and_the_balance_returns()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Wallet", kind = "Asset", role = "Cash", currencyCode = "EUR",
                  openingBalance = 100m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Snacks", kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var created = await (await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 9.99m, categoryId = category!.Id, accountId = bank!.Id,
                  occurredOn = "2026-09-01", description = "Oops" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<TransactionDto>(CancellationToken.None);

        await client.PostAsJsonAsync($"/api/v1/transactions/{created!.Id}/void",
            new { reason = "Entered twice" }, CancellationToken.None);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank.Id}/balance", CancellationToken.None);
        balance!.Balance.Should().Be(100m);

        var withVoided = await client.GetFromJsonAsync<TransactionPageDto>(
            "/api/v1/transactions?includeVoided=true", CancellationToken.None);
        withVoided!.Items.Should().Contain(i => i.Id == created.Id,
            "history is kept; nothing is ever deleted (spec D11)");
    }
}
