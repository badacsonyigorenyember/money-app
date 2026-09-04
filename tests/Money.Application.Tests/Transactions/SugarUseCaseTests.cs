using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;

namespace Money.Application.Tests.Transactions;

public sealed class SugarUseCaseTests : IAsyncLifetime, IDisposable
{
    private readonly UseCaseHarness _harness = new();
    private AccountDto _bank = null!;
    private AccountDto _savings = null!;
    private AccountDto _food = null!;

    public async Task InitializeAsync()
    {
        var accounts = new CreateAccountHandler(_harness.Accounts, _harness.Transactions,
                                                _harness.UnitOfWork, _harness.Clock);
        var categories = new CreateCategoryHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);

        _bank = (await accounts.HandleAsync(new CreateAccountRequest(
            "Current", "Asset", "Bank", null, "EUR", 1000m, new DateOnly(2026, 1, 1)),
            CancellationToken.None)).Value;
        _savings = (await accounts.HandleAsync(new CreateAccountRequest(
            "Rainy day", "Asset", "SavingsPocket", null, "EUR", null, null),
            CancellationToken.None)).Value;
        _food = (await categories.HandleAsync(new CreateCategoryRequest("Food", "Expense", null),
                                              CancellationToken.None)).Value;

        await _harness.Settings.SaveAsync(new Abstractions.AppSettings(
            "EUR", Domain.Periods.PeriodDefinition.Default, 10, true), CancellationToken.None);
        await _harness.UnitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _harness.Dispose();

    private QuickExpenseHandler QuickExpense => new(
        _harness.Accounts, _harness.Transactions, _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    private TransferHandler Transfer => new(
        _harness.Accounts, _harness.Transactions, _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    [Fact]
    public async Task A_quick_expense_needs_only_an_amount_a_category_and_an_account()
    {
        var result = await QuickExpense.HandleAsync(
            new QuickExpenseRequest(12.50m, _food.Id, _bank.Id, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_food.Id, null)).Should().Be(1_250);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(98_750);
    }

    [Fact]
    public async Task A_quick_expense_without_a_date_uses_today_in_the_configured_time_zone()
    {
        // 23:30 UTC on 31 August is already 1 September in Budapest.
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 8, 31, 23, 30, 0, TimeSpan.Zero);

        var result = await QuickExpense.HandleAsync(
            new QuickExpenseRequest(5m, _food.Id, _bank.Id, null, null, null), CancellationToken.None);

        result.Value.OccurredOn.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task A_quick_expense_without_a_description_is_named_after_its_category()
    {
        var result = await QuickExpense.HandleAsync(
            new QuickExpenseRequest(5m, _food.Id, _bank.Id, null, null, null), CancellationToken.None);

        result.Value.Description.Should().Be("Food");
    }

    [Fact]
    public async Task A_quick_expense_against_a_non_category_is_rejected()
    {
        (await QuickExpense.HandleAsync(
            new QuickExpenseRequest(5m, _savings.Id, _bank.Id, null, null, null), CancellationToken.None))
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public async Task A_quick_expense_of_zero_or_less_is_rejected()
    {
        (await QuickExpense.HandleAsync(
            new QuickExpenseRequest(0m, _food.Id, _bank.Id, null, null, null), CancellationToken.None))
            .Error!.Code.Should().Be("transaction.zero_amount");
    }

    [Fact]
    public async Task A_transfer_moves_money_and_is_invisible_to_spending_reports()
    {
        var result = await Transfer.HandleAsync(
            new TransferRequest(200m, _bank.Id, _savings.Id, new DateOnly(2026, 9, 1), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_savings.Id, null)).Should().Be(20_000);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(80_000);
        (await _harness.Queries.SubtreeBalanceAsync("/expense", null, null)).Should().Be(0);
    }

    [Fact]
    public async Task A_transfer_into_a_category_is_rejected()
    {
        (await Transfer.HandleAsync(
            new TransferRequest(10m, _bank.Id, _food.Id, null, null), CancellationToken.None))
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_is_rejected()
    {
        (await Transfer.HandleAsync(
            new TransferRequest(10m, _bank.Id, _bank.Id, null, null), CancellationToken.None))
            .Error!.Code.Should().Be("transaction.duplicate_account");
    }

    [Fact]
    public async Task A_transfer_is_described_in_plain_language_by_default()
    {
        var result = await Transfer.HandleAsync(
            new TransferRequest(200m, _bank.Id, _savings.Id, null, null), CancellationToken.None);

        result.Value.Description.Should().Be("Current to Rainy day");
    }
}
