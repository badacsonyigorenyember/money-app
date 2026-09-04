using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class AccountEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AccountEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task An_account_can_be_created_listed_and_read_back()
    {
        using var client = _factory.CreateApiClient();
        var name = "Erste " + Guid.NewGuid().ToString("N")[..6];

        var created = await client.PostAsJsonAsync("/api/v1/accounts",
            new { name, kind = "Asset", role = "Bank", currencyCode = "EUR", openingBalance = 1000m,
                  openedOn = "2026-01-01" },
            CancellationToken.None);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location.Should().NotBeNull();

        var dto = await created.Content.ReadFromJsonAsync<AccountDto>(
            CancellationToken.None);
        dto!.Path.Should().StartWith("/asset/erste-");

        var listed = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts?kind=Asset", CancellationToken.None);
        listed!.Should().Contain(a => a.Id == dto.Id);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{dto.Id}/balance", CancellationToken.None);
        balance!.Balance.Should().Be(1000m);
    }

    [Fact]
    public async Task An_account_can_be_renamed_and_archived()
    {
        using var client = _factory.CreateApiClient();
        var name = "Cash " + Guid.NewGuid().ToString("N")[..6];

        var dto = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name, kind = "Asset", role = "Cash", currencyCode = "EUR" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var patched = await client.PatchAsJsonAsync($"/api/v1/accounts/{dto!.Id}",
            new { name = name + " renamed" }, CancellationToken.None);
        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        var archived = await client.PostAsync($"/api/v1/accounts/{dto.Id}/archive", null,
                                              CancellationToken.None);
        archived.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listed = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts", CancellationToken.None);
        listed!.Should().NotContain(a => a.Id == dto.Id);

        var withArchived = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts?includeArchived=true", CancellationToken.None);
        withArchived!.Should().Contain(a => a.Id == dto.Id);
    }

    [Fact]
    public async Task A_balance_can_be_asked_for_as_of_a_date()
    {
        using var client = _factory.CreateApiClient();
        var name = "Wallet " + Guid.NewGuid().ToString("N")[..6];

        var dto = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name, kind = "Asset", role = "Cash", currencyCode = "EUR",
                  openingBalance = 50m, openedOn = "2026-06-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var before = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{dto!.Id}/balance?asOf=2026-05-01", CancellationToken.None);

        before!.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task Archiving_an_account_with_active_descendants_is_a_four_hundred_and_nine()
    {
        using var client = _factory.CreateApiClient();
        var name = "Parent Bank " + Guid.NewGuid().ToString("N")[..6];

        var parent = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name, kind = "Asset", role = "Bank", currencyCode = "EUR" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Pocket", kind = "Asset", role = "SavingsPocket", currencyCode = "EUR",
                  parentAccountId = parent!.Id },
            CancellationToken.None);

        var archived = await client.PostAsync($"/api/v1/accounts/{parent.Id}/archive", null,
                                              CancellationToken.None);

        archived.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await archived.Content.ReadAsStringAsync(CancellationToken.None);
        body.Should().Contain("account.archive_blocked_by_active_descendants");
    }
}
