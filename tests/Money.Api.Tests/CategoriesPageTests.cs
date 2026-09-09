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

    /// <summary>
    /// Income and spending are two halves of one page, and which half you are adding to is the
    /// same choice as which half you are looking at. The toggle is a pair of links, so it works
    /// with scripting off and the browser's back button walks between the two.
    /// </summary>
    [Fact]
    public async Task A_toggle_switches_between_creating_spending_and_income_categories()
    {
        using var client = _factory.CreateApiClient();

        var spending = await client.GetStringAsync("/categories", CancellationToken.None);

        spending.Should().Contain("Add a spending category");
        spending.Should().Contain("kind=Income", "the toggle has to offer the other half");

        var income = await client.GetStringAsync("/categories?kind=Income", CancellationToken.None);

        income.Should().Contain("Add an income category");
        income.Should().Contain("kind=Expense");
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
