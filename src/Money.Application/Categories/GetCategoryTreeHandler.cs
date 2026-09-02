using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Domain.Accounts;

namespace Money.Application.Categories;

public sealed class GetCategoryTreeHandler(IAccountRepository accounts)
{
    public async Task<IReadOnlyList<CategoryNodeDto>> HandleAsync(
        string kind, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var parsedKind = Enum.TryParse<AccountKind>(kind, true, out var k) ? k : AccountKind.Expense;

        var all = await accounts.ListAsync(
            parsedKind, AccountRole.Category, includeArchived, cancellationToken);

        var byParent = all.GroupBy(a => a.ParentAccountId)
                          .ToDictionary(g => g.Key ?? Guid.Empty, g => g.ToList());

        return BuildChildren(Guid.Empty, byParent);
    }

    private static CategoryNodeDto[] BuildChildren(
        Guid parentKey, IReadOnlyDictionary<Guid, List<Account>> byParent)
    {
        if (!byParent.TryGetValue(parentKey, out var children)) return [];

        return children
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name, StringComparer.CurrentCulture)
            .Select(a => new CategoryNodeDto(
                a.Id, a.Name, a.Path, a.Kind.ToString(), a.IsArchived, a.ColorHex, a.Icon,
                BuildChildren(a.Id, byParent)))
            .ToArray();
    }
}
