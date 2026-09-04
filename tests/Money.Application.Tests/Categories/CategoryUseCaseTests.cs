using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;

namespace Money.Application.Tests.Categories;

public sealed class CategoryUseCaseTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private CreateCategoryHandler Create => new(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);

    [Fact]
    public async Task A_category_is_an_expense_account_with_the_category_role()
    {
        var result = await Create.HandleAsync(new CreateCategoryRequest("Groceries", "Expense", null),
                                              CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be("Expense");
        result.Value.Role.Should().Be("Category");
        result.Value.Path.Should().Be("/expense/groceries");
    }

    [Fact]
    public async Task Categories_nest_and_come_back_as_a_tree()
    {
        var gaming = (await Create.HandleAsync(new CreateCategoryRequest("Gaming", "Expense", null),
                                               CancellationToken.None)).Value;
        await Create.HandleAsync(new CreateCategoryRequest("Steam", "Expense", gaming.Id),
                                 CancellationToken.None);
        await Create.HandleAsync(new CreateCategoryRequest("Food", "Expense", null),
                                 CancellationToken.None);

        var tree = await new GetCategoryTreeHandler(_harness.Accounts)
            .HandleAsync("Expense", includeArchived: false, CancellationToken.None);

        tree.Should().HaveCount(2);
        tree.Single(n => n.Name == "Gaming").Children.Should().ContainSingle(c => c.Name == "Steam");
        tree.Single(n => n.Name == "Food").Children.Should().BeEmpty();
    }

    [Fact]
    public async Task A_category_cannot_be_created_under_a_parent_of_the_other_kind()
    {
        var salary = (await Create.HandleAsync(new CreateCategoryRequest("Salary", "Income", null),
                                               CancellationToken.None)).Value;

        (await Create.HandleAsync(new CreateCategoryRequest("Groceries", "Expense", salary.Id),
                                  CancellationToken.None))
            .Error!.Code.Should().Be("account.parent_kind_mismatch");
    }

    [Fact]
    public async Task A_category_kind_other_than_income_or_expense_is_rejected()
    {
        (await Create.HandleAsync(new CreateCategoryRequest("Weird", "Asset", null),
                                  CancellationToken.None))
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task The_tree_is_ordered_by_sort_order_then_name()
    {
        await Create.HandleAsync(new CreateCategoryRequest("Zoo", "Expense", null),
                                 CancellationToken.None);
        await Create.HandleAsync(new CreateCategoryRequest("Apples", "Expense", null),
                                 CancellationToken.None);

        var tree = await new GetCategoryTreeHandler(_harness.Accounts)
            .HandleAsync("Expense", false, CancellationToken.None);

        tree.Select(n => n.Name).Should().ContainInOrder("Apples", "Zoo");
    }

    [Fact]
    public async Task Archiving_a_category_with_an_active_child_is_refused_so_the_tree_and_flat_list_agree()
    {
        var gaming = (await Create.HandleAsync(new CreateCategoryRequest("Gaming", "Expense", null),
                                               CancellationToken.None)).Value;
        await Create.HandleAsync(new CreateCategoryRequest("Steam", "Expense", gaming.Id),
                                 CancellationToken.None);

        var archiveResult = await new ArchiveAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(gaming.Id, CancellationToken.None);

        archiveResult.IsSuccess.Should().BeFalse();
        archiveResult.Error!.Code.Should().Be("account.archive_blocked_by_active_descendants");

        // The state F1 identified - an archived parent hiding an active child from the tree
        // while the flat list still reports it - must now be unreachable, not merely unrendered.
        var tree = await new GetCategoryTreeHandler(_harness.Accounts)
            .HandleAsync("Expense", includeArchived: false, CancellationToken.None);
        var flatList = await new ListAccountsHandler(_harness.Accounts)
            .HandleAsync("Expense", "Category", includeArchived: false, CancellationToken.None);

        tree.Single(n => n.Name == "Gaming").Children.Should().ContainSingle(c => c.Name == "Steam");
        flatList.Should().Contain(a => a.Name == "Gaming");
        flatList.Should().Contain(a => a.Name == "Steam");
    }
}
