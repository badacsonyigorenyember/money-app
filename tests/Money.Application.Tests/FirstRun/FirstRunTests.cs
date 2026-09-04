using Money.Application.Contracts;
using Money.Application.FirstRun;
using Money.Application.Settings;

namespace Money.Application.Tests.FirstRun;

public sealed class FirstRunTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private CompleteFirstRunSetupHandler Handler => new(
        _harness.Settings, _harness.Accounts, _harness.Transactions,
        _harness.UnitOfWork, _harness.Clock);

    private static FirstRunRequest ARequest(bool seed = true) => new(
        "EUR", "DayOfMonth", 25, "Europe/Budapest", "Monday",
        "Erste Current", "Bank", 1500.00m, new DateOnly(2026, 1, 1), seed);

    [Fact]
    public async Task First_run_stores_the_settings_marks_itself_complete_and_creates_the_account()
    {
        var result = await Handler.HandleAsync(ARequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FirstRunCompleted.Should().BeTrue();

        var account = await _harness.Accounts.FindByPathAsync("/asset/erste-current");
        account.Should().NotBeNull();
        (await _harness.Queries.BalanceOfAsync(account!.Id, null)).Should().Be(150_000);
    }

    [Fact]
    public async Task First_run_seeds_the_starter_categories()
    {
        await Handler.HandleAsync(ARequest(), CancellationToken.None);

        var expenses = await _harness.Accounts.ListAsync(
            Domain.Accounts.AccountKind.Expense, Domain.Accounts.AccountRole.Category, false);

        expenses.Select(a => a.Name).Should().Contain(
            ["Housing", "Groceries", "Eating out", "Alcohol", "Gaming",
             "Transport", "Health", "Subscriptions", "Other"]);

        var income = await _harness.Accounts.ListAsync(
            Domain.Accounts.AccountKind.Income, Domain.Accounts.AccountRole.Category, false);
        income.Select(a => a.Name).Should().Contain("Salary");
    }

    [Fact]
    public async Task Seeding_can_be_declined()
    {
        await Handler.HandleAsync(ARequest(seed: false), CancellationToken.None);

        (await _harness.Accounts.ListAsync(
            Domain.Accounts.AccountKind.Expense, Domain.Accounts.AccountRole.Category, false))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task The_opening_balance_is_not_spending_and_the_books_balance()
    {
        await Handler.HandleAsync(ARequest(), CancellationToken.None);

        (await _harness.Queries.SubtreeBalanceAsync("/expense", null, null)).Should().Be(0);
        (await _harness.Queries.AllBalancesAsync()).Sum(b => b.BalanceMinor).Should().Be(0);
    }

    [Fact]
    public async Task Running_first_run_twice_is_rejected()
    {
        await Handler.HandleAsync(ARequest(), CancellationToken.None);

        (await Handler.HandleAsync(ARequest(), CancellationToken.None))
            .Error!.Code.Should().Be("settings.already_initialised");
    }

    [Fact]
    public async Task A_bad_anchor_day_aborts_first_run_without_creating_anything()
    {
        var bad = ARequest() with { PeriodAnchorDay = 31 };

        var result = await Handler.HandleAsync(bad, CancellationToken.None);

        result.Error!.Code.Should().Be("period.anchor_day_out_of_range");
        (await _harness.Accounts.ListAllAsync()).Should().BeEmpty();
    }
}
