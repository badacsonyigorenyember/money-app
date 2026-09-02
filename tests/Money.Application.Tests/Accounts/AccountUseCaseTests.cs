using Money.Application.Accounts;
using Money.Application.Contracts;

namespace Money.Application.Tests.Accounts;

public sealed class AccountUseCaseTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private CreateAccountHandler Create => new(_harness.Accounts, _harness.Transactions,
                                               _harness.UnitOfWork, _harness.Clock);

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
}
