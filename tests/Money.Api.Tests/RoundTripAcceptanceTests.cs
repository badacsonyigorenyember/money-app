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

        await client.PostAsJsonAsync("/api/v1/transactions/transfer",
            new { amount = 300m, fromAccountId = bank.Id, toAccountId = savings!.Id,
                  occurredOn = "2026-09-02" }, CancellationToken.None);

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
