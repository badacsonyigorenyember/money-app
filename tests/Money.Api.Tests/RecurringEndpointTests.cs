using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class RecurringEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RecurringEndpointTests(ApiFactory factory) => _factory = factory;

    private static async Task<(Guid Bank, Guid Salary)> SeedAsync(HttpClient client, string suffix)
    {
        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 500m, openedOn = "2026-01-01" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var salary = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Salary " + suffix, kind = "Income" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        return (bank!.Id, salary!.Id);
    }

    private static object MonthlySalary(Guid bank, Guid salary, string description) => new
    {
        direction = "Income",
        amount = 2500m,
        accountId = bank,
        counterpartyId = salary,
        description,
        frequency = "Monthly",
        interval = 1,
        dayOfMonth = 1,
        startDate = "2026-07-01"
    };

    [Fact]
    public async Task A_rule_can_be_created_listed_paused_and_deleted()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (bank, salary) = await SeedAsync(client, suffix);

        var created = await client.PostAsJsonAsync("/api/v1/recurring-rules",
            MonthlySalary(bank, salary, "Salary " + suffix), CancellationToken.None);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var rule = (await created.Content.ReadFromJsonAsync<RecurringRuleDto>(CancellationToken.None))!;
        rule.ScheduleSummary.Should().Be("Every month on the 1st");
        rule.IsActive.Should().BeTrue();

        (await client.PostAsync($"/api/v1/recurring-rules/{rule.Id}/pause", null, CancellationToken.None))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var paused = await client.GetFromJsonAsync<RecurringRuleDto[]>(
            "/api/v1/recurring-rules", CancellationToken.None);

        paused!.Single(r => r.Id == rule.Id).IsActive.Should().BeFalse();

        (await client.DeleteAsync($"/api/v1/recurring-rules/{rule.Id}", CancellationToken.None))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var remaining = await client.GetFromJsonAsync<RecurringRuleDto[]>(
            "/api/v1/recurring-rules", CancellationToken.None);

        remaining!.Should().NotContain(r => r.Id == rule.Id);
    }

    /// <summary>
    /// The catch-up endpoint is safe to hammer: the second and third runs post nothing, and the
    /// ledger holds exactly one entry per due date.
    /// </summary>
    [Fact]
    public async Task Running_the_catch_up_twice_posts_nothing_the_second_time()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (bank, salary) = await SeedAsync(client, suffix);
        var description = "Wages " + suffix;

        await client.PostAsJsonAsync("/api/v1/recurring-rules",
            MonthlySalary(bank, salary, description), CancellationToken.None);

        // The factory's clock is fixed at 1 September 2026, so July, August and September are due.
        await Run(client);
        var afterOne = await CountAsync(client, description);

        await Run(client);
        await Run(client);
        var afterThree = await CountAsync(client, description);

        afterOne.Should().Be(3);
        afterThree.Should().Be(3, "a repeat run must post nothing at all");

        static async Task Run(HttpClient client) =>
            (await client.PostAsync("/api/v1/recurring/run", null, CancellationToken.None))
                .EnsureSuccessStatusCode();

        static async Task<int> CountAsync(HttpClient client, string description)
        {
            var listed = await client.GetFromJsonAsync<TransactionPageDto>(
                $"/api/v1/transactions?q={description}", CancellationToken.None);

            listed!.Items.Should().OnlyContain(item => item.Amount == 2500m);
            return listed.Items.Count;
        }
    }

    [Fact]
    public async Task A_repeat_with_no_gap_is_rejected_with_a_problem_document()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (bank, salary) = await SeedAsync(client, suffix);

        var response = await client.PostAsJsonAsync("/api/v1/recurring-rules", new
        {
            direction = "Income",
            amount = 10m,
            accountId = bank,
            counterpartyId = salary,
            description = "Never " + suffix,
            frequency = "Custom",
            interval = 1,
            startDate = "2026-01-01"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("schedule.custom_step_empty");
    }

}
