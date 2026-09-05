using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class TransactionEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TransactionEndpointTests(ApiFactory factory) => _factory = factory;

    private static async Task<(Guid Bank, Guid Category)> SeedAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 1000m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Food " + suffix, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        return (bank!.Id, category!.Id);
    }

    [Fact]
    public async Task A_quick_expense_can_be_posted_and_appears_in_the_list()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        var created = await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 12.50m, categoryId = category, accountId = bank,
                  occurredOn = "2026-09-01", description = "Lunch" },
            CancellationToken.None);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await client.GetFromJsonAsync<TransactionPageDto>(
            $"/api/v1/transactions?accountId={bank}", CancellationToken.None);

        page!.Items.Should().Contain(i => i.Description == "Lunch" && i.Amount == 12.50m);
    }

    [Fact]
    public async Task A_transfer_can_be_posted_and_is_absent_from_spending()
    {
        using var client = _factory.CreateApiClient();
        var (bank, _) = await SeedAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var savings = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Savings " + suffix, kind = "Asset", role = "SavingsPocket",
                  currencyCode = "EUR" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var created = await client.PostAsJsonAsync("/api/v1/transactions/transfer",
            new { amount = 200m, fromAccountId = bank, toAccountId = savings!.Id,
                  occurredOn = "2026-09-01" }, CancellationToken.None);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{savings.Id}/balance", CancellationToken.None);
        balance!.Balance.Should().Be(200m);
    }

    [Fact]
    public async Task A_split_transaction_can_be_posted_read_edited_and_voided()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        var created = await (await client.PostAsJsonAsync("/api/v1/transactions",
            new
            {
                occurredOn = "2026-09-01",
                description = "Shop",
                lines = new[]
                {
                    new { accountId = category, amount = 60m, memo = (string?)"Food" },
                    new { accountId = bank, amount = -60m, memo = (string?)null }
                }
            }, CancellationToken.None))
            .Content.ReadFromJsonAsync<TransactionDto>(CancellationToken.None);

        created!.Lines.Should().HaveCount(2);

        var fetched = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/v1/transactions/{created.Id}", CancellationToken.None);
        fetched!.Description.Should().Be("Shop");

        var replaced = await client.PutAsJsonAsync($"/api/v1/transactions/{created.Id}",
            new
            {
                occurredOn = "2026-09-02",
                description = "Shop (corrected)",
                lines = new[]
                {
                    new { accountId = category, amount = 65m, memo = (string?)null },
                    new { accountId = bank, amount = -65m, memo = (string?)null }
                }
            }, CancellationToken.None);
        replaced.StatusCode.Should().Be(HttpStatusCode.OK);

        var voided = await client.PostAsJsonAsync($"/api/v1/transactions/{created.Id}/void",
            new { reason = "Entered twice" }, CancellationToken.None);
        voided.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterVoid = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/v1/transactions/{created.Id}", CancellationToken.None);
        afterVoid!.IsVoided.Should().BeTrue();
    }

    [Fact]
    public async Task An_unbalanced_transaction_is_a_four_hundred_naming_the_shortfall()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/transactions",
            new
            {
                occurredOn = "2026-09-01",
                description = "Broken",
                lines = new[]
                {
                    new { accountId = category, amount = 60m },
                    new { accountId = bank, amount = -59m }
                }
            }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("do not balance");
    }

    [Fact]
    public async Task Income_posted_as_a_positive_number_reads_back_as_a_positive_number()
    {
        using var client = _factory.CreateApiClient();
        var (bank, _) = await SeedAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var salary = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Salary " + suffix, kind = "Income" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var created = await (await client.PostAsJsonAsync("/api/v1/transactions",
            new
            {
                occurredOn = "2026-09-01",
                description = "Salary",
                lines = new[]
                {
                    new { accountId = bank, amount = 3000m },
                    new { accountId = salary!.Id, amount = 3000m }
                }
            }, CancellationToken.None))
            .Content.ReadFromJsonAsync<TransactionDto>(CancellationToken.None);

        created!.Lines.Single(l => l.AccountId == salary!.Id).Amount.Should().Be(3000m);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{salary!.Id}/balance", CancellationToken.None);
        balance!.Balance.Should().Be(3000m, "income is shown positive; the sign lives in one mapper");
    }

    [Fact]
    public async Task The_transaction_list_pages_with_a_cursor()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        for (var i = 1; i <= 5; i++)
        {
            await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
                new { amount = i, categoryId = category, accountId = bank,
                      occurredOn = $"2026-09-{i:D2}", description = $"Item {i}" },
                CancellationToken.None);
        }

        var first = await client.GetFromJsonAsync<TransactionPageDto>(
            $"/api/v1/transactions?accountId={bank}&limit=2", CancellationToken.None);

        first!.Items.Should().HaveCount(2);
        first.NextCursor.Should().NotBeNull();

        var second = await client.GetFromJsonAsync<TransactionPageDto>(
            $"/api/v1/transactions?accountId={bank}&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}",
            CancellationToken.None);

        second!.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id));
    }

    // F4: only the success path was covered before. A nonexistent id must 404, not throw or 400.
    [Fact]
    public async Task Voiding_a_nonexistent_transaction_is_a_404()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync($"/api/v1/transactions/{Guid.NewGuid()}/void",
            new { reason = "Doesn't exist" }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        problem.GetProperty("code").GetString().Should().Be("transaction.not_found");
    }

    // F4: voiding twice must be a reported conflict, not a silent no-op or a second void.
    [Fact]
    public async Task Voiding_an_already_voided_transaction_is_a_409()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        var created = await (await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 5m, categoryId = category, accountId = bank,
                  occurredOn = "2026-09-01", description = "To void" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<TransactionDto>(CancellationToken.None);

        var firstVoid = await client.PostAsJsonAsync($"/api/v1/transactions/{created!.Id}/void",
            new { reason = "First" }, CancellationToken.None);
        firstVoid.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondVoid = await client.PostAsJsonAsync($"/api/v1/transactions/{created.Id}/void",
            new { reason = "Second" }, CancellationToken.None);

        secondVoid.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await secondVoid.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        problem.GetProperty("code").GetString().Should().Be("transaction.already_voided");
    }
}
