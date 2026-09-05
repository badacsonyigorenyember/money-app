using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class IdempotencyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public IdempotencyTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_same_idempotency_key_posts_the_expense_only_once()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 1000m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Food " + suffix, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var key = Guid.NewGuid().ToString("N");

        async Task<HttpResponseMessage> PostAsync()
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/v1/transactions/quick-expense")
            {
                Content = JsonContent.Create(new
                {
                    amount = 42m, categoryId = category!.Id, accountId = bank!.Id,
                    occurredOn = "2026-09-01", description = "Retry me"
                })
            };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request, CancellationToken.None);
        }

        var first = await PostAsync();
        var second = await PostAsync();

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.Contains("Idempotency-Replayed").Should().BeTrue();

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank!.Id}/balance", CancellationToken.None);

        balance!.Balance.Should().Be(958m, "the retry must not post a second expense");
    }

    [Fact]
    public async Task Two_different_keys_post_two_expenses()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 100m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Coffee " + suffix, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/v1/transactions/quick-expense")
            {
                Content = JsonContent.Create(new
                {
                    amount = 3m, categoryId = category!.Id, accountId = bank!.Id,
                    occurredOn = "2026-09-01", description = "Coffee"
                })
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            await client.SendAsync(request, CancellationToken.None);
        }

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank!.Id}/balance", CancellationToken.None);

        balance!.Balance.Should().Be(94m);
    }
}
