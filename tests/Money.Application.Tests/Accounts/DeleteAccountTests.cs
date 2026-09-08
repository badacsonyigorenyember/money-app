using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Presentation;
using Money.Application.Transactions;

namespace Money.Application.Tests.Accounts;

/// <summary>
/// Deleting is the one irreversible action in the app, so what it does to the database - and what
/// it deliberately refuses to do to the ledger - is pinned here.
/// </summary>
public sealed class DeleteAccountTests : IAsyncLifetime, IDisposable
{
    private readonly UseCaseHarness _harness = new();
    private AccountDto _bank = null!;

    public async Task InitializeAsync() =>
        _bank = (await Accounts.HandleAsync(
            new CreateAccountRequest("Current", "Asset", "Bank", null, "EUR", 1000m,
                                     new DateOnly(2026, 1, 1)), CancellationToken.None)).Value;

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _harness.Dispose();

    private CreateAccountHandler Accounts => new(_harness.Accounts, _harness.Transactions,
                                                 _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    private CreateCategoryHandler Categories => new(_harness.Accounts, _harness.Settings,
                                                    _harness.UnitOfWork, _harness.Clock);

    private ArchiveAccountHandler Archive =>
        new(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);

    private DeleteAccountHandler Delete =>
        new(_harness.Accounts, _harness.Queries, _harness.UnitOfWork, _harness.Clock);

    private QuickEntryHandler Spend => new(_harness.Accounts, _harness.Transactions,
                                             _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    private async Task<AccountDto> ArchivedCategoryAsync(string name)
    {
        var category = (await Categories.HandleAsync(
            new CreateCategoryRequest(name, "Expense", null), CancellationToken.None)).Value;

        (await Archive.HandleAsync(category.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
        return category;
    }

    [Fact]
    public async Task A_category_nothing_refers_to_is_erased_from_the_database()
    {
        var category = await ArchivedCategoryAsync("Typo");

        var result = await Delete.HandleAsync(category.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Context.Accounts.CountAsync(a => a.Id == category.Id)).Should().Be(0);
    }

    [Fact]
    public async Task A_deleted_name_can_be_used_again()
    {
        // The uniqueness rules apply to the living tree only, or deleting "Groceries" would
        // reserve that name forever.
        var first = await ArchivedCategoryAsync("Groceries");
        await Spend.HandleAsync(
            new QuickEntryRequest(12m, first.Id, _bank.Id, new DateOnly(2026, 9, 1), "Aldi", null),
            CancellationToken.None);

        (await Delete.HandleAsync(first.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var second = await Categories.HandleAsync(
            new CreateCategoryRequest("Groceries", "Expense", null), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        second.Value.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public async Task A_category_the_ledger_names_keeps_its_row_and_its_transactions()
    {
        var category = (await Categories.HandleAsync(
            new CreateCategoryRequest("Gaming", "Expense", null), CancellationToken.None)).Value;

        await Spend.HandleAsync(
            new QuickEntryRequest(20m, category.Id, _bank.Id, new DateOnly(2026, 9, 1), "Steam", null),
            CancellationToken.None);

        (await Archive.HandleAsync(category.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await Delete.HandleAsync(category.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var row = await _harness.Context.Accounts.SingleAsync(a => a.Id == category.Id);
        row.IsDeleted.Should().BeTrue();
        row.Name.Should().Be("Gaming");

        // Deleting a category has never been a way to delete spending.
        (await _harness.Queries.BalanceOfAsync(category.Id, null)).Should().Be(2_000);
    }

    [Fact]
    public async Task History_still_names_a_deleted_category_and_marks_it_deleted()
    {
        var category = (await Categories.HandleAsync(
            new CreateCategoryRequest("Gaming", "Expense", null), CancellationToken.None)).Value;

        await Spend.HandleAsync(
            new QuickEntryRequest(20m, category.Id, _bank.Id, new DateOnly(2026, 9, 1), "Steam", null),
            CancellationToken.None);

        await Archive.HandleAsync(category.Id, CancellationToken.None);
        await Delete.HandleAsync(category.Id, CancellationToken.None);

        var page = await new ListTransactionsHandler(_harness.Queries).HandleAsync(
            new TransactionQuery(null, null, null, null, null, false, null, 50), CancellationToken.None);

        page.Items.Single(item => item.Description == "Steam")
            .CategoryName.Should().Be("Gaming" + AccountDisplayName.DeletedSuffix);
    }

    [Fact]
    public async Task A_deleted_category_is_gone_from_the_tree_and_from_the_archive()
    {
        var category = await ArchivedCategoryAsync("Typo");
        await Delete.HandleAsync(category.Id, CancellationToken.None);

        var tree = new GetCategoryTreeHandler(_harness.Accounts);

        (await tree.HandleAsync("Expense", includeArchived: false, CancellationToken.None))
            .Should().BeEmpty();
        (await tree.HandleAsync("Expense", includeArchived: true, CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_cascades_to_everything_underneath()
    {
        var parent = (await Categories.HandleAsync(
            new CreateCategoryRequest("Leisure", "Expense", null), CancellationToken.None)).Value;
        var child = (await Categories.HandleAsync(
            new CreateCategoryRequest("Gaming", "Expense", parent.Id), CancellationToken.None)).Value;

        await Archive.HandleAsync(child.Id, CancellationToken.None);
        await Archive.HandleAsync(parent.Id, CancellationToken.None);

        (await Delete.HandleAsync(parent.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await _harness.Context.Accounts.CountAsync(a => a.Id == parent.Id || a.Id == child.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_account_still_in_use_cannot_be_deleted_without_archiving_it_first()
    {
        var result = await Delete.HandleAsync(_bank.Id, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("account.delete_needs_archive_first");
        (await _harness.Context.Accounts.CountAsync(a => a.Id == _bank.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Counting_a_subtrees_entries_is_what_tells_the_user_a_delete_is_safe()
    {
        var unused = await ArchivedCategoryAsync("Typo");
        var used = (await Categories.HandleAsync(
            new CreateCategoryRequest("Gaming", "Expense", null), CancellationToken.None)).Value;

        await Spend.HandleAsync(
            new QuickEntryRequest(20m, used.Id, _bank.Id, new DateOnly(2026, 9, 1), "Steam", null),
            CancellationToken.None);

        (await _harness.Queries.SubtreeEntryCountAsync(unused.Path)).Should().Be(0);
        (await _harness.Queries.SubtreeEntryCountAsync(used.Path)).Should().Be(1);
    }
}
