using Money.Domain.Accounts;
using Money.Domain.Money;

namespace Money.Domain.Tests.Accounts;

public sealed class AccountCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static Account Root(string name, AccountKind kind, AccountRole role) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, parent: null, Currency.Eur, Now).Value;

    [Fact]
    public void A_root_account_s_path_is_its_kind_then_its_slug()
    {
        Root("Gaming", AccountKind.Expense, AccountRole.Category).Path.Should().Be("/expense/gaming");
        Root("Erste Current", AccountKind.Asset, AccountRole.Bank).Path.Should().Be("/asset/erste-current");
    }

    [Fact]
    public void A_child_account_s_path_extends_its_parent_s()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        var steam = Account.Create(Guid.CreateVersion7(Now), "Steam", AccountKind.Expense,
                                   AccountRole.Category, gaming, Currency.Eur, Now).Value;

        steam.Path.Should().Be("/expense/gaming/steam");
        steam.ParentAccountId.Should().Be(gaming.Id);
        gaming.ChildPathPrefix.Should().Be("/expense/gaming/");
    }

    [Fact]
    public void A_child_path_prefix_does_not_falsely_match_a_sibling_whose_name_starts_with_the_same_letters()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);
        var gamingPc = Root("Gaming PC", AccountKind.Expense, AccountRole.Category);

        gamingPc.Path.Should().Be("/expense/gaming-pc");
        gamingPc.Path.Should().NotStartWith(gaming.ChildPathPrefix);
    }

    [Theory]
    [InlineData("Eating out", "eating-out")]
    [InlineData("  Spaced  Name  ", "spaced-name")]
    [InlineData("Gaming / Steam", "gaming-steam")]
    [InlineData("Étterem", "étterem")]
    [InlineData("Rent (flat)", "rent-flat")]
    public void Slugs_are_lowercase_and_hyphenated(string name, string expectedSlug)
    {
        Root(name, AccountKind.Expense, AccountRole.Category).Path
            .Should().Be("/expense/" + expectedSlug);
    }

    [Fact]
    public void A_name_with_no_letters_or_digits_cannot_form_a_path_segment()
    {
        Account.Create(Guid.CreateVersion7(Now), "///", AccountKind.Expense, AccountRole.Category,
                       null, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.name_unusable");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_rejected(string name)
    {
        Account.Create(Guid.CreateVersion7(Now), name, AccountKind.Expense, AccountRole.Category,
                       null, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.name_required");
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_rejected()
    {
        Account.Create(Guid.CreateVersion7(Now), new string('x', Account.MaxNameLength + 1),
                       AccountKind.Expense, AccountRole.Category, null, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.name_too_long");
    }

    [Theory]
    [InlineData(AccountKind.Income, AccountRole.Category)]
    [InlineData(AccountKind.Expense, AccountRole.Category)]
    [InlineData(AccountKind.Asset, AccountRole.Bank)]
    [InlineData(AccountKind.Asset, AccountRole.Cash)]
    [InlineData(AccountKind.Asset, AccountRole.SavingsPocket)]
    [InlineData(AccountKind.Asset, AccountRole.Investment)]
    [InlineData(AccountKind.Equity, AccountRole.OpeningBalance)]
    [InlineData(AccountKind.Equity, AccountRole.Adjustment)]
    public void The_legal_kind_and_role_combinations_are_accepted(AccountKind kind, AccountRole role)
    {
        Account.Create(Guid.CreateVersion7(Now), "Whatever", kind, role, null, Currency.Eur, Now)
            .IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(AccountKind.Asset, AccountRole.Category)]
    [InlineData(AccountKind.Expense, AccountRole.Bank)]
    [InlineData(AccountKind.Income, AccountRole.SavingsPocket)]
    [InlineData(AccountKind.Asset, AccountRole.OpeningBalance)]
    [InlineData(AccountKind.Expense, AccountRole.Adjustment)]
    [InlineData(AccountKind.Liability, AccountRole.Bank)]
    public void An_illegal_kind_and_role_combination_is_rejected(AccountKind kind, AccountRole role)
    {
        Account.Create(Guid.CreateVersion7(Now), "Whatever", kind, role, null, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public void A_child_must_share_its_parent_s_kind()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        Account.Create(Guid.CreateVersion7(Now), "Salary", AccountKind.Income, AccountRole.Category,
                       gaming, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.parent_kind_mismatch");
    }

    [Fact]
    public void A_child_must_share_its_parent_s_currency()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        Account.Create(Guid.CreateVersion7(Now), "Steam", AccountKind.Expense, AccountRole.Category,
                       gaming, Currency.Usd, Now)
            .Error!.Code.Should().Be("account.currency_mismatch_with_parent");
    }

    [Fact]
    public void An_archived_parent_cannot_take_new_children()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);
        gaming.Archive(Now);

        Account.Create(Guid.CreateVersion7(Now), "Steam", AccountKind.Expense, AccountRole.Category,
                       gaming, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.parent_archived");
    }

    [Fact]
    public void A_new_account_records_when_it_was_created()
    {
        var account = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        account.CreatedAtUtc.Should().Be(Now);
        account.UpdatedAtUtc.Should().Be(Now);
        account.IsArchived.Should().BeFalse();
        account.IsCategory.Should().BeTrue();
    }

    [Fact]
    public void Archiving_is_idempotent_in_intent_but_reported_as_a_failure_when_repeated()
    {
        var account = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        account.Archive(Now).IsSuccess.Should().BeTrue();
        account.IsArchived.Should().BeTrue();
        account.Archive(Now).Error!.Code.Should().Be("account.already_archived");
    }
}
