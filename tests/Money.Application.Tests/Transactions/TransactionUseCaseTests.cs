using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;

namespace Money.Application.Tests.Transactions;

public sealed class TransactionUseCaseTests : IAsyncLifetime, IDisposable
{
    private readonly UseCaseHarness _harness = new();
    private AccountDto _bank = null!;
    private AccountDto _food = null!;
    private AccountDto _salary = null!;
    private AccountDto _savings = null!;

    public async Task InitializeAsync()
    {
        var accounts = new CreateAccountHandler(_harness.Accounts, _harness.Transactions,
                                                _harness.UnitOfWork, _harness.Clock);
        var categories = new CreateCategoryHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);

        _bank = (await accounts.HandleAsync(
            new CreateAccountRequest("Current", "Asset", "Bank", null, "EUR", 1000m,
                                     new DateOnly(2026, 1, 1)), CancellationToken.None)).Value;
        _savings = (await accounts.HandleAsync(
            new CreateAccountRequest("Rainy day", "Asset", "SavingsPocket", null, "EUR", null, null),
            CancellationToken.None)).Value;
        _food = (await categories.HandleAsync(
            new CreateCategoryRequest("Food", "Expense", null), CancellationToken.None)).Value;
        _salary = (await categories.HandleAsync(
            new CreateCategoryRequest("Salary", "Income", null), CancellationToken.None)).Value;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _harness.Dispose();

    private CreateTransactionHandler Create => new(_harness.Accounts, _harness.Transactions,
                                                   _harness.UnitOfWork, _harness.Clock);

    [Fact]
    public async Task A_balanced_expense_is_saved_and_moves_the_balance()
    {
        var result = await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", "Trattoria",
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(98_000);
        (await _harness.Queries.BalanceOfAsync(_food.Id, null)).Should().Be(2_000);
    }

    [Fact]
    public async Task An_income_line_typed_as_a_positive_number_is_stored_as_a_credit()
    {
        // The user types 3000 next to "Salary". Internally that must become -300000.
        await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "September salary", null,
            [
                new TransactionLineRequest(_bank.Id, 3000m, null),
                new TransactionLineRequest(_salary.Id, 3000m, null)
            ]), CancellationToken.None);

        (await _harness.Queries.BalanceOfAsync(_salary.Id, null)).Should().Be(-300_000);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(400_000);
    }

    [Fact]
    public async Task An_unbalanced_request_is_rejected_and_nothing_is_saved()
    {
        var result = await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Broken", null,
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -19.00m, null)
            ]), CancellationToken.None);

        result.Error!.Code.Should().Be("transaction.does_not_balance");
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(100_000);
    }

    [Fact]
    public async Task A_transaction_can_be_read_back_with_its_lines_named_and_oriented()
    {
        var created = (await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", null,
            [
                new TransactionLineRequest(_food.Id, 20.00m, "Pizza"),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None)).Value;

        var fetched = (await new GetTransactionHandler(_harness.Transactions, _harness.Accounts)
            .HandleAsync(created.Id, CancellationToken.None)).Value;

        fetched.Lines.Should().HaveCount(2);
        fetched.Lines.Single(l => l.AccountId == _food.Id).Amount.Should().Be(20.00m);
        fetched.Lines.Single(l => l.AccountId == _food.Id).AccountName.Should().Be("Food");
        fetched.Lines.Single(l => l.AccountId == _food.Id).Memo.Should().Be("Pizza");
        fetched.Lines.Single(l => l.AccountId == _bank.Id).Amount.Should().Be(-20.00m);
    }

    [Fact]
    public async Task Replacing_a_transaction_changes_the_balances_accordingly()
    {
        var created = (await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", null,
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None)).Value;

        var result = await new ReplaceTransactionHandler(
                _harness.Accounts, _harness.Transactions, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(created.Id, new CreateTransactionRequest(
                new DateOnly(2026, 9, 2), "Dinner (corrected)", null,
                [
                    new TransactionLineRequest(_food.Id, 25.00m, null),
                    new TransactionLineRequest(_bank.Id, -25.00m, null)
                ]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_food.Id, null)).Should().Be(2_500);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(97_500);
    }

    [Fact]
    public async Task Voiding_a_transaction_removes_it_from_every_balance_but_keeps_the_row()
    {
        var created = (await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", null,
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None)).Value;

        var result = await new VoidTransactionHandler(
                _harness.Transactions, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(created.Id, "Entered twice", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(100_000);
        (await _harness.Transactions.FindAsync(created.Id))!.IsVoided.Should().BeTrue();
    }

    [Fact]
    public async Task Voiding_a_transaction_that_does_not_exist_is_a_not_found()
    {
        (await new VoidTransactionHandler(_harness.Transactions, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(Guid.NewGuid(), "Nope", CancellationToken.None))
            .Error!.Code.Should().Be("transaction.not_found");
    }

    [Fact]
    public async Task Moving_money_into_savings_does_not_appear_in_the_expense_total()
    {
        await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "To savings", null,
            [
                new TransactionLineRequest(_savings.Id, 500.00m, null),
                new TransactionLineRequest(_bank.Id, -500.00m, null)
            ]), CancellationToken.None);

        (await _harness.Queries.SubtreeBalanceAsync("/expense", null, null)).Should().Be(0);
        (await _harness.Queries.BalanceOfAsync(_savings.Id, null)).Should().Be(50_000);
    }

    [Fact]
    public async Task The_list_shows_a_transaction_with_its_category_and_account_names()
    {
        await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", "Trattoria",
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None);

        var page = await new ListTransactionsHandler(_harness.Queries).HandleAsync(
            new TransactionQuery(null, null, null, null, null, false, null, 20), CancellationToken.None);

        var row = page.Items.Single(i => i.Description == "Dinner");
        row.CategoryName.Should().Be("Food");
        row.AccountName.Should().Be("Current");
        row.Amount.Should().Be(20.00m);
    }

    [Fact]
    public async Task The_list_shows_an_income_transaction_as_a_positive_amount()
    {
        // Neither line is Kind=Expense, so the list's "headline" picker has no expense line to
        // fall back on. It must still land on the asset leg (Bank, +3000), not the income
        // category's raw, not-yet-display-oriented credit (-300000 minor).
        await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "September salary", null,
            [
                new TransactionLineRequest(_bank.Id, 3000m, null),
                new TransactionLineRequest(_salary.Id, 3000m, null)
            ]), CancellationToken.None);

        var page = await new ListTransactionsHandler(_harness.Queries).HandleAsync(
            new TransactionQuery(null, null, null, null, null, false, null, 20), CancellationToken.None);

        var row = page.Items.Single(i => i.Description == "September salary");
        row.Amount.Should().Be(3000m, "income must never render as a negative number in the list");
        row.AccountName.Should().Be("Current");
    }

    [Fact]
    public async Task The_list_shows_an_opening_balance_as_a_positive_amount_on_the_asset_account()
    {
        // _bank was created in InitializeAsync with an opening balance of 1000 EUR, which books
        // Bank (Asset, +100000) against an "Opening balance" Equity account (-100000). The row's
        // Amount must reflect the account it names growing, not an arbitrary tied-amount leg.
        var page = await new ListTransactionsHandler(_harness.Queries).HandleAsync(
            new TransactionQuery(null, null, null, null, "Opening", true, null, 20), CancellationToken.None);

        var row = page.Items.Single();
        row.AccountName.Should().Be("Current");
        row.Amount.Should().Be(1000.00m,
            "a deposit into an asset account must show as positive, matching the sign convention");
    }
}
