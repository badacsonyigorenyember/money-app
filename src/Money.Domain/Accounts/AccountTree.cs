using Money.Domain.Primitives;

namespace Money.Domain.Accounts;

/// <summary>
/// Pure tree operations. Every mutation that changes a path rewrites the whole subtree in the
/// same call, so a materialised path can never go stale.
/// </summary>
public static class AccountTree
{
    public static IReadOnlyList<Account> DescendantsOf(Account root, IEnumerable<Account> all)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(all);

        var prefix = root.ChildPathPrefix;
        return all.Where(a => a.Path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    }

    public static Result Rename(
        Account account, string newName,
        IReadOnlyCollection<Account> siblings, IReadOnlyCollection<Account> descendants,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(account);

        var validated = Account.ValidateName(newName);
        if (validated.IsFailure) return Result.Fail(validated.Error!);

        var slug = Account.Slugify(validated.Value);
        if (slug.Length == 0) return DomainErrors.Account.NameUnusable(newName);

        if (FindCollision(slug, siblings, account.Id) is not null)
            return DomainErrors.Account.DuplicateSiblingName(validated.Value);

        var oldPath = account.Path;
        var newPath = ParentPrefixOf(oldPath) + slug;

        account.ApplyRename(validated.Value, newPath, nowUtc);
        RewriteDescendants(descendants, oldPath, newPath, nowUtc);

        return Result.Ok();
    }

    public static Result Move(
        Account account, Account? newParent,
        IReadOnlyCollection<Account> newSiblings, IReadOnlyCollection<Account> descendants,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (newParent is not null)
        {
            if (newParent.Id == account.Id
                || newParent.Path.StartsWith(account.ChildPathPrefix, StringComparison.Ordinal))
            {
                return DomainErrors.Account.CannotReparentUnderOwnDescendant();
            }

            if (newParent.Kind != account.Kind)
                return DomainErrors.Account.ParentKindMismatch(
                    account.Kind.ToString(), newParent.Kind.ToString());

            if (newParent.IsArchived) return DomainErrors.Account.ParentArchived(newParent.Name);

            if (!string.Equals(newParent.CurrencyCode, account.CurrencyCode, StringComparison.Ordinal))
                return DomainErrors.Account.CurrencyMismatchWithParent();
        }

        var slug = Account.Slugify(account.Name);
        if (FindCollision(slug, newSiblings, account.Id) is not null)
            return DomainErrors.Account.DuplicateSiblingName(account.Name);

        var oldPath = account.Path;
        var newPath = Account.PathFor(newParent, account.Kind, slug);

        account.ApplyMove(newParent?.Id, newPath, nowUtc);
        RewriteDescendants(descendants, oldPath, newPath, nowUtc);

        return Result.Ok();
    }

    private static Account? FindCollision(
        string slug, IReadOnlyCollection<Account> siblings, Guid selfId) =>
        siblings.FirstOrDefault(s =>
            s.Id != selfId && string.Equals(Account.Slugify(s.Name), slug, StringComparison.Ordinal));

    private static string ParentPrefixOf(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return path[..(lastSlash + 1)];
    }

    private static void RewriteDescendants(
        IEnumerable<Account> descendants, string oldPath, string newPath, DateTimeOffset nowUtc)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return;

        foreach (var descendant in descendants)
        {
            descendant.ApplyPath(newPath + descendant.Path[oldPath.Length..], nowUtc);
        }
    }
}
