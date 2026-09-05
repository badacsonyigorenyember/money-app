using System.Net;
using System.Net.Http.Json;
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
        html.Should().Contain("Transactions");
        html.Should().Contain("htmx.min.js");
    }

    [Fact]
    public async Task No_page_uses_accounting_vocabulary()
    {
        using var client = _factory.CreateApiClient();

        // Task 32 widens this list to "/categories", "/accounts" and "/settings" once those
        // pages exist. Every page ever added goes in here.
        foreach (var path in new[] { "/transactions" })
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

        await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
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
}
