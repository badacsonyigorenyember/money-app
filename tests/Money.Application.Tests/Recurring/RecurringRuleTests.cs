using Microsoft.EntityFrameworkCore;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Recurring;
using Money.Domain.Ledger;

namespace Money.Application.Tests.Recurring;

public sealed class RecurringRuleTests : IAsyncLifetime, IDisposable
{
    private readonly UseCaseHarness _harness = new();
    private AccountDto _bank = null!;
    private AccountDto _savings = null!;
    private AccountDto _salary = null!;
    private AccountDto _rent = null!;

    public async Task InitializeAsync()
    {
        var accounts = new CreateAccountHandler(_harness.Accounts, _harness.Transactions,
                                                _harness.Settings, _harness.UnitOfWork, _harness.Clock);
        var categories = new CreateCategoryHandler(_harness.Accounts, _harness.Settings,
                                                   _harness.UnitOfWork, _harness.Clock);

        _bank = (await accounts.HandleAsync(new CreateAccountRequest(
            "Current", "Asset", "Bank", null, "EUR", 1000m, new DateOnly(2026, 1, 1)),
            CancellationToken.None)).Value;
        _savings = (await accounts.HandleAsync(new CreateAccountRequest(
            "Rainy day", "Asset", "SavingsPocket", null, "EUR", null, null),
            CancellationToken.None)).Value;
        _salary = (await categories.HandleAsync(
            new CreateCategoryRequest("Salary", "Income", null), CancellationToken.None)).Value;
        _rent = (await categories.HandleAsync(
            new CreateCategoryRequest("Rent", "Expense", null), CancellationToken.None)).Value;

        await _harness.Settings.SaveAsync(new Abstractions.AppSettings(
            "EUR", Domain.Periods.PeriodDefinition.Default, 10, true), CancellationToken.None);
        await _harness.UnitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _harness.Dispose();

    private CreateRecurringRuleHandler Create => new(
        _harness.Accounts, _harness.RecurringRules, _harness.Settings,
        _harness.UnitOfWork, _harness.Clock);

    private ListRecurringRulesHandler List => new(
        _harness.RecurringRules, _harness.Accounts, _harness.Settings, _harness.Clock);

    private UpdateRecurringRuleHandler Update => new(
        _harness.RecurringRules, _harness.UnitOfWork, _harness.Clock);

    private RecurringMaterialiser Materialiser => new(
        _harness.RecurringRules, _harness.Accounts, _harness.Transactions,
        _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    private CreateRecurringRuleRequest MonthlySalary(int dayOfMonth = 1) => new(
        "Income", 3000m, _bank.Id, _salary.Id, "Salary", "Acme Ltd",
        "Monthly", 1, null, null, dayOfMonth, null, 0, 0, 0,
        new DateOnly(2026, 1, dayOfMonth), null);

    private async Task<IReadOnlyList<Transaction>> LedgerAsync() =>
        await _harness.Transactions.ListAllAsync(CancellationToken.None);

    private async Task<DateOnly[]> RecurringDatesAsync() =>
        (await LedgerAsync())
            .Where(t => t.SourceKind == TransactionSourceKind.Recurring)
            .Select(t => t.OccurredOn)
            .OrderBy(d => d)
            .ToArray();

    [Fact]
    public async Task A_salary_rule_posts_income_the_right_way_round()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

        (await Create.HandleAsync(MonthlySalary(), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        (await Materialiser.RunAsync(CancellationToken.None)).Should().Be(1);

        var posted = (await LedgerAsync()).Single(t => t.SourceKind == TransactionSourceKind.Recurring);

        posted.OccurredOn.Should().Be(new DateOnly(2026, 1, 1));
        posted.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(300_000);
        posted.Postings.Single(p => p.AccountId == _salary.Id).AmountMinor.Should().Be(-300_000);
    }

    /// <summary>
    /// The single most important safety property of the feature (spec 5.8): rent cannot be posted
    /// twice, however many times the materialiser runs.
    /// </summary>
    [Fact]
    public async Task Running_the_materialiser_three_times_posts_each_occurrence_once()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);

        await Create.HandleAsync(MonthlySalary(), CancellationToken.None);

        var first = await Materialiser.RunAsync(CancellationToken.None);
        var second = await Materialiser.RunAsync(CancellationToken.None);
        var third = await Materialiser.RunAsync(CancellationToken.None);

        first.Should().Be(3, "January, February and March are all due");
        second.Should().Be(0);
        third.Should().Be(0);

        (await RecurringDatesAsync()).Should().HaveCount(3);
    }

    /// <summary>
    /// The watermark alone would hide a double-post if it were ever lost. This winds it back and
    /// proves the pre-check against the ledger catches the occurrence on its own.
    /// </summary>
    [Fact]
    public async Task An_occurrence_already_in_the_ledger_is_not_posted_again_after_a_rewind()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 2, 20, 9, 0, 0, TimeSpan.Zero);

        await Create.HandleAsync(MonthlySalary(), CancellationToken.None);
        (await Materialiser.RunAsync(CancellationToken.None)).Should().Be(2);

        await _harness.Context.Database.ExecuteSqlRawAsync(
            "UPDATE RecurringRules SET LastMaterialisedThrough = NULL");
        _harness.Context.ChangeTracker.Clear();

        (await Materialiser.RunAsync(CancellationToken.None)).Should().Be(0);
        (await RecurringDatesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_day_31_rule_lands_on_the_last_day_of_february()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero);

        await Create.HandleAsync(new CreateRecurringRuleRequest(
            "Expense", 800m, _bank.Id, _rent.Id, "Rent", null,
            "Monthly", 1, null, null, 31, null, 0, 0, 0,
            new DateOnly(2026, 1, 31), null), CancellationToken.None);

        await Materialiser.RunAsync(CancellationToken.None);

        (await RecurringDatesAsync())
            .Should().Equal(new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28));
    }

    [Fact]
    public async Task A_transfer_rule_moves_money_between_two_accounts()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 1, 13, 9, 0, 0, TimeSpan.Zero);

        await Create.HandleAsync(new CreateRecurringRuleRequest(
            "Transfer", 100m, _bank.Id, _savings.Id, "Save something", null,
            "Weekly", 1, "Monday", null, null, null, 0, 0, 0,
            new DateOnly(2026, 1, 1), null), CancellationToken.None);

        await Materialiser.RunAsync(CancellationToken.None);

        (await RecurringDatesAsync())
            .Should().Equal(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 12));

        var first = (await LedgerAsync())
            .First(t => t.SourceKind == TransactionSourceKind.Recurring);

        first.Postings.Single(p => p.AccountId == _savings.Id).AmountMinor.Should().Be(10_000);
        first.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(-10_000);
    }

    [Fact]
    public async Task A_paused_rule_posts_nothing()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);

        var created = (await Create.HandleAsync(MonthlySalary(), CancellationToken.None)).Value;
        (await Update.SetActiveAsync(created.Id, false, CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        (await Materialiser.RunAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task A_rule_stops_at_its_end_date()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 6, 30, 9, 0, 0, TimeSpan.Zero);

        await Create.HandleAsync(MonthlySalary() with { EndDate = new DateOnly(2026, 3, 15) },
                                 CancellationToken.None);

        (await Materialiser.RunAsync(CancellationToken.None)).Should().Be(3);
    }

    [Fact]
    public async Task Deleting_a_rule_leaves_the_transactions_it_already_posted_alone()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);

        var created = (await Create.HandleAsync(MonthlySalary(), CancellationToken.None)).Value;
        await Materialiser.RunAsync(CancellationToken.None);

        (await Update.DeleteAsync(created.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await List.HandleAsync(true, CancellationToken.None)).Should().BeEmpty();
        (await RecurringDatesAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task The_rules_list_says_when_the_next_one_is_due()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);

        await Create.HandleAsync(MonthlySalary(10), CancellationToken.None);

        var rule = (await List.HandleAsync(true, CancellationToken.None)).Single();

        rule.NextOccurrence.Should().Be(new DateOnly(2026, 4, 10));
        rule.ScheduleSummary.Should().Be("Every month on the 10th");
        rule.Amount.Should().Be(3000m);
    }

    [Fact]
    public async Task A_custom_repeat_of_nothing_is_rejected()
    {
        var result = await Create.HandleAsync(new CreateRecurringRuleRequest(
            "Expense", 10m, _bank.Id, _rent.Id, "Nothing", null,
            "Custom", 1, null, null, null, null, 0, 0, 0,
            new DateOnly(2026, 1, 1), null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("schedule.custom_step_empty");
    }

    [Fact]
    public async Task An_expense_rule_cannot_name_an_income_category()
    {
        var result = await Create.HandleAsync(new CreateRecurringRuleRequest(
            "Expense", 10m, _bank.Id, _salary.Id, "Wrong way round", null,
            "Monthly", 1, null, null, 1, null, 0, 0, 0,
            new DateOnly(2026, 1, 1), null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("account.not_a_category");
    }
}
