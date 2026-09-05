using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Domain.Periods;

namespace Money.Application.Tests.Accounts;

public sealed class AccountUseCaseTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private CreateAccountHandler Create => new(_harness.Accounts, _harness.Transactions,
                                               _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    [Fact]
    public async Task Creating_a_bank_account_persists_it_with_a_path()
    {
        var result = await Create.HandleAsync(
            new CreateAccountRequest("Erste Current", "Asset", "Bank", null, "EUR", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().Be("/asset/erste-current");
        (await _harness.Accounts.FindAsync(result.Value.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task An_account_created_with_no_currency_inherits_the_ledgers_base_currency()
    {
        // C1: after first run, the settings row's BaseCurrencyCode is the single source of truth
        // for "what currency by default" - not a hardcoded EUR literal. A HUF user must get a
        // HUF account, or recording against it fails at Transaction.cs (CurrencyMismatchWithAccount)
        // and the account can never be fixed (nothing can change an account's currency).
        await _harness.Settings.SaveAsync(new AppSettings(
            "HUF", PeriodDefinition.Default, BackupRetentionCount: 10, FirstRunCompleted: true));
        await _harness.UnitOfWork.SaveChangesAsync(CancellationToken.None);

        var result = await Create.HandleAsync(
            new CreateAccountRequest("Wallet", "Asset", "Cash", null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrencyCode.Should().Be("HUF");
    }

    [Fact]
    public async Task A_root_category_created_with_no_currency_inherits_the_ledgers_base_currency()
    {
        await _harness.Settings.SaveAsync(new AppSettings(
            "HUF", PeriodDefinition.Default, BackupRetentionCount: 10, FirstRunCompleted: true));
        await _harness.UnitOfWork.SaveChangesAsync(CancellationToken.None);

        var category = new CreateCategoryHandler(_harness.Accounts, _harness.Settings,
                                                  _harness.UnitOfWork, _harness.Clock);
        var result = await category.HandleAsync(
            new CreateCategoryRequest("Groceries", "Expense", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrencyCode.Should().Be("HUF");
    }

    [Fact]
    public async Task An_opening_balance_dated_by_default_uses_the_configured_time_zones_today_not_utc()
    {
        // I1: CreateAccountHandler used to date the opening balance with
        // DateOnly.FromDateTime(now.UtcDateTime) - a second, ad hoc "what is today" computation
        // that bypasses PeriodResolver (CLAUDE.md: nothing else does period/day arithmetic). For
        // a Budapest user recording just after midnight local time, UTC is still the day before.
        await _harness.Settings.SaveAsync(new AppSettings(
            "EUR", PeriodDefinition.Default, BackupRetentionCount: 10, FirstRunCompleted: true));
        await _harness.UnitOfWork.SaveChangesAsync(CancellationToken.None);

        // 2026-09-01 22:30 UTC is 2026-09-02 00:30 in Europe/Budapest (CEST, UTC+2) -
        // PeriodDefinition.Default's time zone.
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 9, 1, 22, 30, 0, TimeSpan.Zero);

        var result = await Create.HandleAsync(
            new CreateAccountRequest("Wallet", "Asset", "Cash", null, "EUR", 100m, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var opening = (await _harness.Transactions.ListAllAsync())
            .Single(t => t.Description == "Opening balance");
        opening.OccurredOn.Should().Be(new DateOnly(2026, 9, 2));
    }

    [Fact]
    public async Task Creating_a_bank_account_with_an_opening_balance_posts_the_opening_transaction()
    {
        var result = await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", 250.00m, new DateOnly(2026, 1, 1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(result.Value.Id, null)).Should().Be(25_000);

        // The counter-entry is an Equity/OpeningBalance account, created on demand.
        var equity = await _harness.Accounts.FindFirstByRoleAsync(Domain.Accounts.AccountRole.OpeningBalance);
        equity.Should().NotBeNull();
        (await _harness.Queries.BalanceOfAsync(equity!.Id, null)).Should().Be(-25_000);
    }

    [Fact]
    public async Task An_opening_balance_never_shows_up_as_spending()
    {
        var account = (await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", 250.00m, new DateOnly(2026, 1, 1)),
            CancellationToken.None)).Value;

        var expenseTotal = await _harness.Queries.SubtreeBalanceAsync("/expense", null, null);

        expenseTotal.Should().Be(0);
        account.Should().NotBeNull();
    }

    [Fact]
    public async Task An_illegal_kind_and_role_pair_is_rejected_before_it_reaches_the_database()
    {
        var result = await Create.HandleAsync(
            new CreateAccountRequest("Nonsense", "Asset", "Category", null, "EUR", null, null),
            CancellationToken.None);

        result.Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task An_unparsable_kind_is_rejected_with_a_useful_code()
    {
        var result = await Create.HandleAsync(
            new CreateAccountRequest("Nonsense", "Bananas", "Bank", null, "EUR", null, null),
            CancellationToken.None);

        result.Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task A_duplicate_sibling_name_is_rejected()
    {
        await Create.HandleAsync(new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", null, null),
                                 CancellationToken.None);

        var second = await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", null, null),
            CancellationToken.None);

        second.Error!.Code.Should().Be("account.duplicate_sibling_name");
    }

    [Fact]
    public async Task Renaming_an_account_rewrites_its_descendants_paths_in_the_database()
    {
        var gaming = (await Create.HandleAsync(
            new CreateAccountRequest("Gaming", "Expense", "Category", null, "EUR", null, null),
            CancellationToken.None)).Value;

        var steam = (await Create.HandleAsync(
            new CreateAccountRequest("Steam", "Expense", "Category", gaming.Id, "EUR", null, null),
            CancellationToken.None)).Value;

        var patch = new PatchAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);
        var result = await patch.HandleAsync(gaming.Id,
            new PatchAccountRequest("Games", null, null, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Accounts.FindAsync(steam.Id))!.Path.Should().Be("/expense/games/steam");
    }

    [Fact]
    public async Task Renaming_and_reparenting_in_one_request_rewrites_every_descendants_path()
    {
        // C2: PatchAccountHandler re-read descendants after the rename branch had already
        // mutated account.Path in memory, so DescendantsOfAsync's SQL prefix match ran against
        // the still-unsaved (old) paths in the database and came back empty - the Move branch
        // then rewrote only the node itself, leaving every descendant with a stale path.
        var fun = (await Create.HandleAsync(
            new CreateAccountRequest("Fun", "Expense", "Category", null, "EUR", null, null),
            CancellationToken.None)).Value;
        var saving = (await Create.HandleAsync(
            new CreateAccountRequest("Saving", "Expense", "Category", null, "EUR", null, null),
            CancellationToken.None)).Value;

        var gaming = (await Create.HandleAsync(
            new CreateAccountRequest("Gaming", "Expense", "Category", fun.Id, "EUR", null, null),
            CancellationToken.None)).Value;
        var steam = (await Create.HandleAsync(
            new CreateAccountRequest("Steam", "Expense", "Category", gaming.Id, "EUR", null, null),
            CancellationToken.None)).Value;
        var wallet = (await Create.HandleAsync(
            new CreateAccountRequest("Wallet", "Expense", "Category", steam.Id, "EUR", null, null),
            CancellationToken.None)).Value;

        var patch = new PatchAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);
        var result = await patch.HandleAsync(gaming.Id,
            new PatchAccountRequest("Games", saving.Id, null, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Accounts.FindAsync(gaming.Id))!.Path.Should().Be("/expense/saving/games");
        (await _harness.Accounts.FindAsync(steam.Id))!.Path.Should().Be("/expense/saving/games/steam");
        (await _harness.Accounts.FindAsync(wallet.Id))!.Path.Should().Be("/expense/saving/games/steam/wallet");
    }

    [Fact]
    public async Task Archiving_an_account_hides_it_from_the_default_listing_but_keeps_it()
    {
        var cash = (await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", null, null),
            CancellationToken.None)).Value;

        await new ArchiveAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(cash.Id, CancellationToken.None);

        var list = new ListAccountsHandler(_harness.Accounts);
        (await list.HandleAsync(null, null, includeArchived: false,
                                CancellationToken.None))
            .Should().NotContain(a => a.Id == cash.Id);
        (await list.HandleAsync(null, null, includeArchived: true,
                                CancellationToken.None))
            .Should().Contain(a => a.Id == cash.Id);
    }

    [Fact]
    public async Task An_account_balance_can_be_asked_for_as_of_a_date()
    {
        var cash = (await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", 100m, new DateOnly(2026, 6, 1)),
            CancellationToken.None)).Value;

        var handler = new GetAccountBalanceHandler(_harness.Accounts, _harness.Queries);

        (await handler.HandleAsync(cash.Id, new DateOnly(2026, 5, 1),
                                   CancellationToken.None)).Value.Balance
            .Should().Be(0m);
        (await handler.HandleAsync(cash.Id, new DateOnly(2026, 7, 1),
                                   CancellationToken.None)).Value.Balance
            .Should().Be(100m);
    }

    [Fact]
    public async Task Asking_for_the_balance_of_an_account_that_does_not_exist_is_a_not_found()
    {
        var handler = new GetAccountBalanceHandler(_harness.Accounts, _harness.Queries);

        (await handler.HandleAsync(Guid.NewGuid(), null, CancellationToken.None))
            .Error!.Code.Should().Be("account.not_found");
    }

    [Fact]
    public async Task Archiving_a_leaf_account_succeeds()
    {
        var cash = (await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", null, null),
            CancellationToken.None)).Value;

        var result = await new ArchiveAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(cash.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Accounts.FindAsync(cash.Id))!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Archiving_a_parent_with_an_active_child_is_refused()
    {
        var gaming = (await Create.HandleAsync(
            new CreateAccountRequest("Gaming", "Expense", "Category", null, "EUR", null, null),
            CancellationToken.None)).Value;
        await Create.HandleAsync(
            new CreateAccountRequest("Steam", "Expense", "Category", gaming.Id, "EUR", null, null),
            CancellationToken.None);

        var result = await new ArchiveAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(gaming.Id, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("account.archive_blocked_by_active_descendants");
        (await _harness.Accounts.FindAsync(gaming.Id))!.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Archiving_a_parent_whose_only_descendant_is_already_archived_succeeds()
    {
        var gaming = (await Create.HandleAsync(
            new CreateAccountRequest("Gaming", "Expense", "Category", null, "EUR", null, null),
            CancellationToken.None)).Value;
        var steam = (await Create.HandleAsync(
            new CreateAccountRequest("Steam", "Expense", "Category", gaming.Id, "EUR", null, null),
            CancellationToken.None)).Value;

        var archive = new ArchiveAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);
        await archive.HandleAsync(steam.Id, CancellationToken.None);

        var result = await archive.HandleAsync(gaming.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Accounts.FindAsync(gaming.Id))!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task A_similarly_named_sibling_does_not_block_archiving()
    {
        var gaming = (await Create.HandleAsync(
            new CreateAccountRequest("Gaming", "Expense", "Category", null, "EUR", null, null),
            CancellationToken.None)).Value;
        await Create.HandleAsync(
            new CreateAccountRequest("Gaming PC", "Expense", "Category", null, "EUR", null, null),
            CancellationToken.None);

        var result = await new ArchiveAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(gaming.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
