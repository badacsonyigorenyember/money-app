using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class CategoryEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CategoryEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Categories_can_be_created_and_come_back_as_a_tree()
    {
        using var client = _factory.CreateApiClient();
        var parentName = "Gaming " + Guid.NewGuid().ToString("N")[..6];

        var parent = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = parentName, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var child = await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Steam", kind = "Expense", parentCategoryId = parent!.Id },
            CancellationToken.None);
        child.StatusCode.Should().Be(HttpStatusCode.Created);

        var tree = await client.GetFromJsonAsync<List<CategoryNodeDto>>(
            "/api/v1/categories?kind=Expense", CancellationToken.None);

        tree!.Single(n => n.Id == parent.Id).Children.Should().ContainSingle(c => c.Name == "Steam");
    }

    [Fact]
    public async Task The_api_never_uses_accounting_vocabulary_for_categories()
    {
        using var client = _factory.CreateApiClient();

        var body = await client.GetStringAsync("/api/v1/categories?kind=Expense",
                                               CancellationToken.None);

        body.Should().NotContain("posting", "the ledger's vocabulary must not leak into the API");
        body.Should().NotContain("debit");
    }
}
