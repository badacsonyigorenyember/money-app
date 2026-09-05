using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Categories;

/// <summary>
/// Sugar over accounts. A category is an Income or Expense account with Role=Category; the UI
/// never says "account" about one.
/// </summary>
public sealed class CreateCategoryHandler(
    IAccountRepository accounts, ISettingsRepository settings, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<AccountDto>> HandleAsync(
        CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = AccountMapper.ParseKind(request.Kind);
        if (kind.IsFailure) return kind.Error!;

        if (!Account.IsLegalCombination(kind.Value, AccountRole.Category))
            return DomainErrors.Account.KindRoleMismatch(kind.Value.ToString(), nameof(AccountRole.Category));

        Account? parent = null;
        if (request.ParentCategoryId is { } parentId)
        {
            parent = await accounts.FindAsync(parentId, cancellationToken);
            if (parent is null) return DomainErrors.Account.NotFound(parentId);
            if (!parent.IsCategory) return DomainErrors.Account.NotACategory(parent.Name);
        }

        var currency = parent is null
            ? Currency.FromCode(await BaseCurrencyResolver.BaseCurrencyCodeAsync(settings, cancellationToken)).Value
            : Currency.FromCode(parent.CurrencyCode).Value;

        var siblings = await accounts.ChildrenOfAsync(request.ParentCategoryId, cancellationToken);
        var slug = Account.Slugify(request.Name ?? "");
        if (siblings.Any(s => string.Equals(Account.Slugify(s.Name), slug, StringComparison.Ordinal)))
            return DomainErrors.Account.DuplicateSiblingName(request.Name ?? "");

        var now = clock.UtcNow;
        var created = Account.Create(
            Guid.CreateVersion7(now), request.Name ?? "", kind.Value, AccountRole.Category,
            parent, currency, now);

        if (created.IsFailure) return created.Error!;

        // Unlike accounts, categories default to SortOrder=0 rather than insertion order: the
        // category tree's default ordering is alphabetical (SortOrder, then Name), and manual
        // reordering is a later feature. Assigning an incrementing SortOrder here would defeat
        // that alphabetical fallback for every newly created sibling.
        created.Value.UpdatePresentation(0, null, null, null, null, now);
        accounts.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AccountDto>.Ok(AccountMapper.ToDto(created.Value));
    }
}
