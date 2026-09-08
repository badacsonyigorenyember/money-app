using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Import;
using Money.Domain.Primitives;

namespace Money.Application.Tests.Import;

/// <summary>
/// The import use case against real SQLite. The feed itself is faked - what is under test is the
/// mapping, the dedup and the window, not HTTP.
/// </summary>
public sealed class ImportBankTransactionsTests : IAsyncLifetime, IDisposable
{
    private readonly UseCaseHarness _harness = new();
    private readonly FakeBankFeed _feed = new();
    private AccountDto _bank = null!;

    public async Task InitializeAsync()
    {
        var accounts = new CreateAccountHandler(_harness.Accounts, _harness.Transactions,
                                                _harness.Settings, _harness.UnitOfWork, _harness.Clock);

        _bank = (await accounts.HandleAsync(new CreateAccountRequest(
            "OTP current", "Asset", "Bank", null, "EUR", null, null),
            CancellationToken.None)).Value;

        await _harness.Settings.SaveAsync(new AppSettings(
            "EUR", Domain.Periods.PeriodDefinition.Default, 10, true), CancellationToken.None);
        await _harness.UnitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _harness.Dispose();

    private ImportBankTransactionsHandler Handler => new(
        _harness.Accounts, _harness.Transactions, _feed,
        _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    private Task<Result<ImportResultDto>> ImportAsync(int? windowDays = null) =>
        Handler.HandleAsync(new ImportBankTransactionsRequest(_bank.Id, windowDays),
                            CancellationToken.None);

    private static BankTransaction Line(
        string reference, long amountMinor, string currency = "EUR", int day = 1) =>
        new(reference, new DateOnly(2026, 9, day), amountMinor, currency,
            amountMinor < 0 ? "SPAR 1234" : "Salary", amountMinor < 0 ? "SPAR" : "Employer Kft");

    [Fact]
    public async Task Money_leaving_the_account_lands_on_the_account_and_unclassified_spending()
    {
        _feed.Returns(Line("eb:a", -2_000));

        var result = await ImportAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Imported.Should().Be(1);

        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(-2_000);

        var unclassified = await _harness.Accounts.FindByPathAsync("/expense/unclassified");
        unclassified.Should().NotBeNull();
        (await _harness.Queries.BalanceOfAsync(unclassified!.Id, null)).Should().Be(2_000);
    }

    [Fact]
    public async Task Money_arriving_lands_on_the_account_and_unclassified_income()
    {
        _feed.Returns(Line("eb:b", 300_000));

        await ImportAsync();

        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(300_000);

        var unclassified = await _harness.Accounts.FindByPathAsync("/income/unclassified");
        unclassified.Should().NotBeNull();
        // Income is stored credit-negative. Presentation negates it; the ledger does not.
        (await _harness.Queries.BalanceOfAsync(unclassified!.Id, null)).Should().Be(-300_000);
    }

    [Fact]
    public async Task Re_running_the_same_window_imports_nothing_the_second_time()
    {
        _feed.Returns(Line("eb:a", -2_000), Line("eb:b", -3_000));

        var first = await ImportAsync();
        var second = await ImportAsync();

        first.Value.Imported.Should().Be(2);
        second.Value.Imported.Should().Be(0);
        second.Value.AlreadyPresent.Should().Be(2);

        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(-5_000);
    }

    [Fact]
    public async Task An_overlapping_window_imports_only_what_is_new()
    {
        _feed.Returns(Line("eb:a", -2_000));
        await ImportAsync();

        _feed.Returns(Line("eb:a", -2_000), Line("eb:c", -1_500));
        var second = await ImportAsync();

        second.Value.Imported.Should().Be(1);
        second.Value.AlreadyPresent.Should().Be(1);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(-3_500);
    }

    [Fact]
    public async Task A_reference_repeated_inside_one_batch_is_imported_once()
    {
        // The hash fallback for a feed that supplies no entry_reference can collide. The unique
        // index would reject the whole SaveChanges, so the batch is deduplicated before it.
        _feed.Returns(Line("eb:same", -2_000), Line("eb:same", -2_000));

        var result = await ImportAsync();

        result.Value.Imported.Should().Be(1);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(-2_000);
    }

    [Fact]
    public async Task A_line_in_another_currency_is_skipped_rather_than_mangled()
    {
        _feed.Returns(Line("eb:a", -2_000), Line("eb:usd", -5_000, currency: "USD"));

        var result = await ImportAsync();

        result.Value.Imported.Should().Be(1);
        result.Value.SkippedForeignCurrency.Should().Be(1);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(-2_000);
    }

    [Fact]
    public async Task The_default_window_is_the_last_35_days_ending_today()
    {
        // The harness clock is 2026-09-01; the settings time zone is Europe/Budapest.
        await ImportAsync();

        _feed.LastFrom.Should().Be(new DateOnly(2026, 7, 29));
        _feed.LastTo.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task The_window_can_be_widened_and_is_range_checked()
    {
        await ImportAsync(windowDays: 10);
        _feed.LastFrom.Should().Be(new DateOnly(2026, 8, 23));

        (await ImportAsync(windowDays: 0)).Error!.Code.Should().Be("import.window_out_of_range");
        (await ImportAsync(windowDays: 400)).Error!.Code.Should().Be("import.window_out_of_range");
    }

    [Fact]
    public async Task Importing_into_a_category_rather_than_an_account_is_rejected()
    {
        var categories = new CreateCategoryHandler(_harness.Accounts, _harness.Settings,
                                                   _harness.UnitOfWork, _harness.Clock);
        var food = (await categories.HandleAsync(new CreateCategoryRequest("Food", "Expense", null),
                                                 CancellationToken.None)).Value;

        var result = await Handler.HandleAsync(
            new ImportBankTransactionsRequest(food.Id, null), CancellationToken.None);

        result.Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task An_unknown_account_is_rejected()
    {
        var result = await Handler.HandleAsync(
            new ImportBankTransactionsRequest(Guid.NewGuid(), null), CancellationToken.None);

        result.Error!.Code.Should().Be("account.not_found");
    }

    [Fact]
    public async Task A_feed_failure_is_returned_rather_than_thrown()
    {
        _feed.Fails(new DomainError("bankfeed.unavailable", "OTP is not answering."));

        var result = await ImportAsync();

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("bankfeed.unavailable");
    }

    [Fact]
    public async Task An_imported_transaction_keeps_the_bank_description_and_payee()
    {
        _feed.Returns(Line("eb:a", -2_000));

        await ImportAsync();

        var transaction = (await _harness.Transactions.ListAllAsync()).Single();
        transaction.Description.Should().Be("SPAR 1234");
        transaction.Payee.Should().Be("SPAR");
        transaction.ExternalRef.Should().Be("eb:a");
        transaction.SourceKind.Should().Be(Domain.Ledger.TransactionSourceKind.Import);
        transaction.OccurredOn.Should().Be(new DateOnly(2026, 9, 1));
    }

    private sealed class FakeBankFeed : IBankFeed
    {
        private IReadOnlyList<BankTransaction> _lines = [];
        private DomainError? _error;

        public DateOnly LastFrom { get; private set; }
        public DateOnly LastTo { get; private set; }

        public void Returns(params BankTransaction[] lines)
        {
            _lines = lines;
            _error = null;
        }

        public void Fails(DomainError error) => _error = error;

        public Task<Result<IReadOnlyList<BankTransaction>>> FetchBookedAsync(
            DateOnly fromInclusive, DateOnly toInclusive, CancellationToken cancellationToken = default)
        {
            LastFrom = fromInclusive;
            LastTo = toInclusive;

            return Task.FromResult(_error is not null
                ? Result<IReadOnlyList<BankTransaction>>.Fail(_error)
                : Result<IReadOnlyList<BankTransaction>>.Ok(_lines));
        }
    }
}
