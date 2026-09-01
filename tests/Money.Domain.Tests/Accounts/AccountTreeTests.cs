using Money.Domain.Accounts;
using Money.Domain.Money;

namespace Money.Domain.Tests.Accounts;

public sealed class AccountTreeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private static Account New(string name, Account? parent = null,
                               AccountKind kind = AccountKind.Expense,
                               AccountRole role = AccountRole.Category) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, parent, Currency.Eur, Now).Value;

    [Fact]
    public void Descendants_are_everything_under_the_path_prefix_and_never_the_node_itself()
    {
        var gaming = New("Gaming");
        var steam = New("Steam", gaming);
        var deck = New("Deck", steam);
        var food = New("Food");

        var all = new[] { gaming, steam, deck, food };

        AccountTree.DescendantsOf(gaming, all).Should().BeEquivalentTo(new[] { steam, deck });
        AccountTree.DescendantsOf(food, all).Should().BeEmpty();
    }

    [Fact]
    public void A_prefix_that_only_looks_like_a_parent_is_not_a_descendant()
    {
        var gaming = New("Gaming");
        var gamingChairs = New("Gaming chairs");

        AccountTree.DescendantsOf(gaming, new[] { gaming, gamingChairs }).Should().BeEmpty();
    }

    [Fact]
    public void Renaming_rewrites_the_node_s_path_and_every_descendant_path()
    {
        var gaming = New("Gaming");
        var steam = New("Steam", gaming);
        var deck = New("Deck", steam);
        var descendants = new[] { steam, deck };

        var result = AccountTree.Rename(gaming, "Games", siblings: [], descendants, Later);

        result.IsSuccess.Should().BeTrue();
        gaming.Name.Should().Be("Games");
        gaming.Path.Should().Be("/expense/games");
        steam.Path.Should().Be("/expense/games/steam");
        deck.Path.Should().Be("/expense/games/steam/deck");
        deck.UpdatedAtUtc.Should().Be(Later);
    }

    [Fact]
    public void Renaming_to_a_name_a_sibling_already_uses_is_rejected()
    {
        var gaming = New("Gaming");
        var food = New("Food");

        AccountTree.Rename(gaming, "Food", siblings: [food], descendants: [], Later)
            .Error!.Code.Should().Be("account.duplicate_sibling_name");
        gaming.Name.Should().Be("Gaming");
    }

    [Fact]
    public void Renaming_to_a_name_that_only_differs_by_case_or_punctuation_is_rejected()
    {
        var gaming = New("Gaming");
        var eatingOut = New("Eating out");

        AccountTree.Rename(gaming, "eating-out", siblings: [eatingOut], descendants: [], Later)
            .Error!.Code.Should().Be("account.duplicate_sibling_name");
    }

    [Fact]
    public void Renaming_a_node_to_its_own_current_name_is_allowed()
    {
        var gaming = New("Gaming");

        AccountTree.Rename(gaming, "Gaming", siblings: [gaming], descendants: [], Later)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Moving_a_node_reparents_it_and_rewrites_the_whole_subtree()
    {
        var gaming = New("Gaming");
        var hobbies = New("Hobbies");
        var steam = New("Steam", gaming);
        var deck = New("Deck", steam);

        var result = AccountTree.Move(gaming, hobbies, newSiblings: [], descendants: [steam, deck], Later);

        result.IsSuccess.Should().BeTrue();
        gaming.ParentAccountId.Should().Be(hobbies.Id);
        gaming.Path.Should().Be("/expense/hobbies/gaming");
        steam.Path.Should().Be("/expense/hobbies/gaming/steam");
        deck.Path.Should().Be("/expense/hobbies/gaming/steam/deck");
    }

    [Fact]
    public void Moving_a_node_to_the_root_puts_it_back_under_its_kind()
    {
        var hobbies = New("Hobbies");
        var gaming = New("Gaming", hobbies);
        var steam = New("Steam", gaming);

        AccountTree.Move(gaming, newParent: null, newSiblings: [], descendants: [steam], Later)
            .IsSuccess.Should().BeTrue();

        gaming.ParentAccountId.Should().BeNull();
        gaming.Path.Should().Be("/expense/gaming");
        steam.Path.Should().Be("/expense/gaming/steam");
    }

    [Fact]
    public void A_node_cannot_be_moved_underneath_its_own_descendant()
    {
        var gaming = New("Gaming");
        var steam = New("Steam", gaming);

        AccountTree.Move(gaming, steam, newSiblings: [], descendants: [steam], Later)
            .Error!.Code.Should().Be("account.cannot_reparent_under_own_descendant");
        gaming.Path.Should().Be("/expense/gaming");
    }

    [Fact]
    public void A_node_cannot_be_moved_underneath_itself()
    {
        var gaming = New("Gaming");

        AccountTree.Move(gaming, gaming, newSiblings: [], descendants: [], Later)
            .Error!.Code.Should().Be("account.cannot_reparent_under_own_descendant");
    }

    [Fact]
    public void A_node_cannot_be_moved_under_a_parent_of_a_different_kind()
    {
        var expense = New("Gaming");
        var income = New("Salary", kind: AccountKind.Income);

        AccountTree.Move(expense, income, newSiblings: [], descendants: [], Later)
            .Error!.Code.Should().Be("account.parent_kind_mismatch");
    }

    [Fact]
    public void A_node_cannot_be_moved_under_an_archived_parent()
    {
        var gaming = New("Gaming");
        var hobbies = New("Hobbies");
        hobbies.Archive(Now);

        AccountTree.Move(gaming, hobbies, newSiblings: [], descendants: [], Later)
            .Error!.Code.Should().Be("account.parent_archived");
    }

    [Fact]
    public void A_move_that_would_collide_with_an_existing_sibling_is_rejected()
    {
        var gaming = New("Gaming");
        var hobbies = New("Hobbies");
        var existingGaming = New("Gaming", hobbies);

        AccountTree.Move(gaming, hobbies, newSiblings: [existingGaming], descendants: [], Later)
            .Error!.Code.Should().Be("account.duplicate_sibling_name");
    }
}
