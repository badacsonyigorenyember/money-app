using System.Globalization;
using System.Text;
using Money.Domain.Money;
using Money.Domain.Primitives;

namespace Money.Domain.Accounts;

/// <summary>
/// A node in the single account tree. Categories are accounts too (Kind=Income|Expense,
/// Role=Category); that is what gives budgets and reports free hierarchy and subtree rollups.
/// The UI calls these "categories" and never says "account" about them.
/// </summary>
public sealed class Account
{
    public const int MaxNameLength = 120;

    // EF Core materialisation constructor. Never call this from domain code.
    private Account()
    {
        Name = null!;
        Path = null!;
        CurrencyCode = null!;
    }

    private Account(
        Guid id, string name, AccountKind kind, AccountRole role,
        Guid? parentAccountId, string path, string currencyCode, DateTimeOffset nowUtc)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Role = role;
        ParentAccountId = parentAccountId;
        Path = path;
        CurrencyCode = currencyCode;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public AccountKind Kind { get; private set; }
    public AccountRole Role { get; private set; }
    public Guid? ParentAccountId { get; private set; }

    /// <summary>Materialised path, e.g. "/expense/gaming/steam". Subtree queries are prefix matches.</summary>
    public string Path { get; private set; }

    public string CurrencyCode { get; private set; }
    public bool IsArchived { get; private set; }
    public DateOnly? OpenedOn { get; private set; }
    public int SortOrder { get; private set; }
    public string? ColorHex { get; private set; }
    public string? Icon { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public string ChildPathPrefix => Path + "/";

    public bool IsCategory => Role == AccountRole.Category;

    public static Result<Account> Create(
        Guid id, string name, AccountKind kind, AccountRole role,
        Account? parent, Currency currency, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var validatedName = ValidateName(name);
        if (validatedName.IsFailure) return validatedName.Error!;

        if (!IsLegalCombination(kind, role))
            return DomainErrors.Account.KindRoleMismatch(kind.ToString(), role.ToString());

        var slug = Slugify(validatedName.Value);
        if (slug.Length == 0) return DomainErrors.Account.NameUnusable(name);

        if (parent is not null)
        {
            if (parent.Kind != kind)
                return DomainErrors.Account.ParentKindMismatch(kind.ToString(), parent.Kind.ToString());
            if (parent.IsArchived) return DomainErrors.Account.ParentArchived(parent.Name);
            if (!string.Equals(parent.CurrencyCode, currency.Code, StringComparison.Ordinal))
                return DomainErrors.Account.CurrencyMismatchWithParent();
        }

        var path = parent is null ? RootPathFor(kind) + "/" + slug : parent.ChildPathPrefix + slug;

        return Result<Account>.Ok(new Account(
            id, validatedName.Value, kind, role, parent?.Id, path, currency.Code, nowUtc));
    }

    public Result Archive(DateTimeOffset nowUtc)
    {
        if (IsArchived) return DomainErrors.Account.AlreadyArchived(Name);

        IsArchived = true;
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    public Result Restore(DateTimeOffset nowUtc)
    {
        IsArchived = false;
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    public Result UpdatePresentation(
        int sortOrder, string? colorHex, string? icon, string? notes, DateOnly? openedOn,
        DateTimeOffset nowUtc)
    {
        SortOrder = sortOrder;
        ColorHex = colorHex;
        Icon = icon;
        Notes = notes;
        OpenedOn = openedOn;
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    internal void ApplyRename(string newName, string newPath, DateTimeOffset nowUtc)
    {
        Name = newName;
        Path = newPath;
        UpdatedAtUtc = nowUtc;
    }

    internal void ApplyMove(Guid? newParentId, string newPath, DateTimeOffset nowUtc)
    {
        ParentAccountId = newParentId;
        Path = newPath;
        UpdatedAtUtc = nowUtc;
    }

    internal void ApplyPath(string newPath, DateTimeOffset nowUtc)
    {
        Path = newPath;
        UpdatedAtUtc = nowUtc;
    }

    public static string RootPathFor(AccountKind kind) =>
        "/" + kind.ToString().ToLowerInvariant();

    /// <summary>Lower-cases, keeps letters and digits, and collapses everything else into single hyphens.</summary>
    public static string Slugify(string name)
    {
        var builder = new StringBuilder(name.Length);
        var pendingHyphen = false;

        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                pendingHyphen = false;
                builder.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return builder.ToString();
    }

    internal static Result<string> ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return DomainErrors.Account.NameRequired();
        if (trimmed.Length > MaxNameLength) return DomainErrors.Account.NameTooLong(MaxNameLength);
        return Result<string>.Ok(trimmed);
    }

    /// <summary>
    /// Public because it is a genuine domain question that callers outside this assembly ask:
    /// the category use case (Task 21) checks a pair before building anything.
    /// </summary>
    public static bool IsLegalCombination(AccountKind kind, AccountRole role) => role switch
    {
        AccountRole.Category => kind is AccountKind.Income or AccountKind.Expense,
        AccountRole.Bank or AccountRole.Cash or AccountRole.SavingsPocket or AccountRole.Investment
            => kind is AccountKind.Asset,
        AccountRole.OpeningBalance or AccountRole.Adjustment => kind is AccountKind.Equity,
        _ => false
    };

    public override string ToString() => $"{Name} ({Path})";
}
