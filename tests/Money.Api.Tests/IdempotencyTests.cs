using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    // F1: a sequential test cannot show this race - both requests must be in flight before
    // either completes. If the fix regresses (reservation happens after the handler runs
    // instead of before it) this either creates two transactions, or flakes depending on
    // scheduling, which is exactly the bug being guarded against.
    [Fact]
    public async Task Concurrent_requests_with_the_same_key_create_only_one_transaction()
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

        HttpRequestMessage BuildRequest()
        {
            var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/v1/transactions/quick-expense")
            {
                Content = JsonContent.Create(new
                {
                    amount = 42m, categoryId = category!.Id, accountId = bank!.Id,
                    occurredOn = "2026-09-01", description = "Race"
                })
            };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        using var requestA = BuildRequest();
        using var requestB = BuildRequest();

        // Both requests are dispatched, and therefore in flight, before either is awaited.
        var taskA = client.SendAsync(requestA, CancellationToken.None);
        var taskB = client.SendAsync(requestB, CancellationToken.None);
        var responses = await Task.WhenAll(taskA, taskB);

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1,
            "exactly one of the two truly concurrent requests may create the expense");

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank!.Id}/balance", CancellationToken.None);

        balance!.Balance.Should().Be(958m,
            "a genuinely concurrent duplicate must never post a second expense, " +
            "not just a sequential retry");
    }

    // F2: the store is keyed on (key, endpoint) only; without a body-hash check, reusing a key
    // for a materially different request silently replays the first response instead of
    // reporting the mismatch.
    [Fact]
    public async Task The_same_key_with_a_different_body_is_rejected_as_a_conflict()
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

        async Task<HttpResponseMessage> PostAsync(decimal amount)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/v1/transactions/quick-expense")
            {
                Content = JsonContent.Create(new
                {
                    amount, categoryId = category!.Id, accountId = bank!.Id,
                    occurredOn = "2026-09-01", description = "Mismatch"
                })
            };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request, CancellationToken.None);
        }

        var first = await PostAsync(42m);
        var second = await PostAsync(99m);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        problem.GetProperty("code").GetString()
            .Should().Be("idempotency.key_reused_with_different_payload");

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank!.Id}/balance", CancellationToken.None);
        balance!.Balance.Should().Be(958m, "the mismatched retry must not post its own expense either");
    }

    // F3: the store is keyed on (key, endpoint), so the same key against two different routes
    // must be treated as two unrelated operations.
    [Fact]
    public async Task The_same_key_on_different_endpoints_is_two_different_operations()
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

        using var quickExpenseRequest = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/transactions/quick-expense")
        {
            Content = JsonContent.Create(new
            {
                amount = 10m, categoryId = category!.Id, accountId = bank!.Id,
                occurredOn = "2026-09-01", description = "Quick"
            })
        };
        quickExpenseRequest.Headers.Add("Idempotency-Key", key);

        using var fullTransactionRequest = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new
            {
                occurredOn = "2026-09-01",
                description = "Full",
                lines = new[]
                {
                    new { accountId = category!.Id, amount = 15m },
                    new { accountId = bank!.Id, amount = -15m }
                }
            })
        };
        fullTransactionRequest.Headers.Add("Idempotency-Key", key);

        var quickResponse = await client.SendAsync(quickExpenseRequest, CancellationToken.None);
        var fullResponse = await client.SendAsync(fullTransactionRequest, CancellationToken.None);

        quickResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        fullResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank!.Id}/balance", CancellationToken.None);
        balance!.Balance.Should().Be(1000m - 10m - 15m,
            "the same key against two different endpoints is two different operations");
    }
}
