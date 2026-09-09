namespace Money.Api.Tests;

public sealed class CategoriesPageTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CategoriesPageTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_categories_page_renders_the_tree()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var parent = await _factory.CreateCategoryAsync("Travel " + suffix, "Expense");
        await _factory.CreateCategoryAsync("Flights", "Expense", parent.Id);

        var html = await client.GetStringAsync("/categories", CancellationToken.None);

        html.Should().Contain("Travel " + suffix);
        html.Should().Contain("Flights");
    }

    [Fact]
    public async Task The_accounts_page_shows_balances()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        await _factory.CreateAccountAsync(
            "Vault " + suffix, "Cash", openingBalance: 42m, openedOn: new DateOnly(2026, 1, 1));

        var html = await client.GetStringAsync("/accounts", CancellationToken.None);

        html.Should().Contain("Vault " + suffix);
        html.Should().Contain("42.00");
    }

    [Fact]
    public async Task The_accounts_page_never_shows_the_equity_plumbing()
    {
        using var client = _factory.CreateApiClient();

        var html = await client.GetStringAsync("/accounts", CancellationToken.None);

        html.Should().NotContain("Equity", "the opening-balance account is bookkeeping, not a user account");
    }
}
