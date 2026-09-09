using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;
using Money.Domain.Periods;
using Money.TestSupport;

namespace Money.Application.Tests.Accounts;

/// <summary>
/// The home page's chart and its figures. What matters here is that the numbers a reader sees are
/// positive and attributed to the right account, that the line starts from what the previous
/// month left behind, that it stops at today rather than running flat into the future, and that a
/// transfer between two accounts moves the line without being mistaken for income or spending.
/// </summary>
public sealed class AccountOverviewTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private CreateAccountHandler CreateAccount => new(
        _harness.Accounts, _harness.Transactions, _harness.Settings,
        _harness.UnitOfWork, _harness.Clock);

    private CreateCategoryHandler CreateCategory => new(
        _harness.Accounts, _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    private QuickEntryHandler Record => new(
        _harness.Accounts, _harness.Transactions, _harness.Settings,
        _harness.UnitOfWork, _harness.Clock);

    private TransferHandler Transfer => new(
        _harness.Accounts, _harness.Transactions, _harness.Settings,
        _harness.UnitOfWork, _harness.Clock);

    private FixedExchangeRates _rates = FixedExchangeRates.None;

    private GetAccountOverviewHandler Overview => new(
        _harness.Accounts, _harness.Queries, _harness.Settings, _rates, _harness.Clock);

    [Fact]
    public async Task Each_account_reports_its_own_money_in_out_and_kept()
    {
        await SettleAsync();

        var current = await NewAccountAsync("Current", "Bank");
        var wallet = await NewAccountAsync("Wallet", "Cash");
        var salary = await NewCategoryAsync("Salary", "Income");
        var food = await NewCategoryAsync("Food", "Expense");

        // The clock is 2026-09-01, so "today" is inside the September period.
        await RecordAsync(3000m, salary, current);
        await RecordAsync(200m, food, current);
        await RecordAsync(50m, food, wallet);

        var month = await Overview.HandleAsync(null, CancellationToken.None);

        var bank = month.Accounts.Single(a => a.Name == "Current");
        bank.MoneyIn.Should().Be(3000m);
        bank.MoneyOut.Should().Be(200m);
        bank.Kept.Should().Be(2800m);

        var cash = month.Accounts.Single(a => a.Name == "Wallet");
        cash.MoneyIn.Should().Be(0m);
        cash.MoneyOut.Should().Be(50m);
        cash.Kept.Should().Be(-50m);
    }

    [Fact]
    public async Task Moving_money_between_accounts_is_not_income_or_spending_but_still_moves_the_line()
    {
        // I12 restated for this read model: a transfer has no Kind=Expense and no Kind=Income leg,
        // so funding a savings pocket must not read as a month where 500 was earned and spent.
        // The balance line is a different question and must still show the money arriving.
        await SettleAsync();

        var current = await NewAccountAsync("Current", "Bank");
        var savings = await NewAccountAsync("Savings", "SavingsPocket");

        var moved = await Transfer.HandleAsync(
            new TransferRequest(500m, current, savings, null, "To savings"),
            CancellationToken.None);
        moved.IsSuccess.Should().BeTrue(moved.Error?.Message);

        var month = await Overview.HandleAsync(null, CancellationToken.None);

        month.Accounts.Should().OnlyContain(a => a.MoneyIn == 0m && a.MoneyOut == 0m);

        month.Accounts.Single(a => a.Name == "Savings").Days[^1].Balance.Should().Be(500m);
        month.Accounts.Single(a => a.Name == "Current").Days[^1].Balance.Should().Be(-500m);
    }

    [Fact]
    public async Task A_month_starts_the_line_from_what_the_previous_month_left()
    {
        await SettleAsync();

        var current = await NewAccountAsync("Current", "Bank");
        var salary = await NewCategoryAsync("Salary", "Income");
        var food = await NewCategoryAsync("Food", "Expense");

        await RecordAsync(1000m, salary, current, new DateOnly(2026, 8, 20));
        await RecordAsync(40m, food, current, new DateOnly(2026, 9, 1));

        var account = (await Overview.HandleAsync(null, CancellationToken.None)).Accounts.Single();

        account.OpeningBalance.Should().Be(1000m, "August closed there and September opens from it");
        account.ClosingBalance.Should().Be(960m);
        account.MoneyIn.Should().Be(0m, "August's salary is not September's income");
        account.MoneyOut.Should().Be(40m);
    }

    [Fact]
    public async Task The_line_stops_at_today_rather_than_running_into_the_future()
    {
        // A flat line drawn out to the 30th would claim a balance for days that have not happened.
        await SettleAsync();

        _harness.Clock.UtcNow = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

        var current = await NewAccountAsync("Current", "Bank");
        var food = await NewCategoryAsync("Food", "Expense");

        await RecordAsync(30m, food, current, new DateOnly(2026, 9, 4));

        var month = await Overview.HandleAsync(null, CancellationToken.None);
        var account = month.Accounts.Single();

        month.DayCount.Should().Be(30, "the axis still spans the whole of September");
        account.Days.Should().HaveCount(10, "but only the 1st to the 10th have happened");
        account.Days[^1].Day.Should().Be(new DateOnly(2026, 9, 10));

        // A day with no entries carries the previous day forward rather than dropping to zero.
        account.Days[2].Balance.Should().Be(0m);
        account.Days[3].Balance.Should().Be(-30m);
        account.Days[^1].Balance.Should().Be(-30m);
    }

    [Fact]
    public async Task Paging_offers_a_way_back_always_and_a_way_forward_only_into_the_past()
    {
        await SettleAsync();
        await NewAccountAsync("Current", "Bank");

        var now = await Overview.HandleAsync(null, CancellationToken.None);
        now.PeriodKey.Should().Be("2026-M09");
        now.PreviousKey.Should().Be("2026-M08");
        now.NextKey.Should().BeNull("there is nothing useful to page forward into");

        var august = await Overview.HandleAsync("2026-M08", CancellationToken.None);
        august.Label.Should().StartWith("August");
        august.PreviousKey.Should().Be("2026-M07");
        august.NextKey.Should().Be("2026-M09");
        august.DayCount.Should().Be(31);
        august.Accounts.Single().Days.Should().HaveCount(31, "August is over, so all of it is drawn");
    }

    [Fact]
    public async Task A_key_that_makes_no_sense_lands_on_the_month_in_progress()
    {
        // It arrives from a URL, and a URL edited by hand should land somewhere sensible.
        await SettleAsync();
        await NewAccountAsync("Current", "Bank");

        (await Overview.HandleAsync("nonsense", CancellationToken.None)).PeriodKey.Should().Be("2026-M09");
        (await Overview.HandleAsync("2026-W12", CancellationToken.None)).PeriodKey.Should().Be("2026-M09");
    }

    [Fact]
    public async Task An_account_in_another_currency_carries_the_rate_that_puts_it_on_the_axis()
    {
        // The chart is drawn in one currency. The read model does not convert anything itself -
        // it hands the view the rate, and the account's own amounts stay as the account holds
        // them, so the node can still show what is actually in the account.
        await SettleAsync();
        _rates = new FixedExchangeRates(("HUF", "EUR", 0.00275m));

        var home = await NewAccountAsync("Current", "Bank", "EUR", 1000m);
        var abroad = await NewAccountAsync("Budapest", "Bank", "HUF", 400_000m);

        var month = await Overview.HandleAsync(null, CancellationToken.None);

        month.BaseCurrencyCode.Should().Be("EUR");
        month.Accounts.Single(a => a.AccountId == home).RateToBase.Should().Be(1m);

        var huf = month.Accounts.Single(a => a.AccountId == abroad);
        huf.CurrencyCode.Should().Be("HUF");
        huf.ClosingBalance.Should().Be(400_000m, "the account holds forints, and says so");
        huf.RateToBase.Should().Be(0.00275m);
        (huf.ClosingBalance * huf.RateToBase!.Value).Should().Be(1100m);
    }

    [Fact]
    public async Task An_account_with_no_rate_to_convert_by_is_left_off_the_chart()
    {
        // Offline, or a currency the source does not quote. The row is still reported - the
        // figures below the chart are in the account's own currency and need no rate - but a
        // null rate is the signal that no line can honestly be drawn on a euro axis.
        await SettleAsync();
        _rates = FixedExchangeRates.None;

        await NewAccountAsync("Budapest", "Bank", "HUF", 400_000m);

        var month = await Overview.HandleAsync(null, CancellationToken.None);

        month.Accounts.Single().RateToBase.Should().BeNull();
    }

    // --- helpers ---------------------------------------------------------------

    private async Task SettleAsync()
    {
        await _harness.Settings.SaveAsync(new AppSettings(
            "EUR", PeriodDefinition.Default, BackupRetentionCount: 10, FirstRunCompleted: true));
        await _harness.UnitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid> NewAccountAsync(
        string name, string role, string currency = "EUR", decimal? opening = null)
    {
        var created = await CreateAccount.HandleAsync(
            new CreateAccountRequest(name, "Asset", role, null, currency, opening, null),
            CancellationToken.None);

        created.IsSuccess.Should().BeTrue(created.Error?.Message);
        return created.Value.Id;
    }

    private async Task<Guid> NewCategoryAsync(string name, string kind)
    {
        var created = await CreateCategory.HandleAsync(
            new CreateCategoryRequest(name, kind, null), CancellationToken.None);

        created.IsSuccess.Should().BeTrue(created.Error?.Message);
        return created.Value.Id;
    }

    private async Task RecordAsync(decimal amount, Guid category, Guid account, DateOnly? on = null)
    {
        var result = await Record.HandleAsync(
            new QuickEntryRequest(amount, category, account, on, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }
}
