using System.Collections.ObjectModel;
using Money.Domain.Accounts;
using Money.Domain.Primitives;

namespace Money.Domain.Ledger;

/// <summary>
/// The invariant boundary of the ledger. There is no other way to build a transaction, so
/// an unbalanced one (I1) or a one-sided one (I2) is unrepresentable, and entries against an
/// archived account (I4) cannot be created.
/// </summary>
public sealed class Transaction
{
    private readonly List<Posting> _postings = [];

    // EF Core materialisation constructor.
    private Transaction() => Description = null!;

    private Transaction(
        Guid id, DateOnly occurredOn, string description, string? payee,
        TransactionSourceKind sourceKind, Guid? sourceId, DateTimeOffset nowUtc)
    {
        Id = id;
        OccurredOn = occurredOn;
        Description = description;
        Payee = payee;
        SourceKind = sourceKind;
        SourceId = sourceId;
        BookedAtUtc = nowUtc;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public DateTimeOffset BookedAtUtc { get; private set; }
    public string Description { get; private set; }
    public string? Payee { get; private set; }
    public TransactionSourceKind SourceKind { get; private set; }
    public Guid? SourceId { get; private set; }
    public string? ExternalRef { get; private set; }
    public bool IsVoided { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<Posting> Postings => new ReadOnlyCollection<Posting>(_postings);

    public static Result<Transaction> Create(
        Guid id, DateOnly occurredOn, string description, string? payee,
        TransactionSourceKind sourceKind, Guid? sourceId,
        IReadOnlyList<PostingDraft> postings,
        IReadOnlyDictionary<Guid, Account> accountsById,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(postings);
        ArgumentNullException.ThrowIfNull(accountsById);

        var trimmedDescription = description?.Trim();
        if (string.IsNullOrEmpty(trimmedDescription))
            return DomainErrors.Transaction.DescriptionRequired();

        var transaction = new Transaction(
            id, occurredOn, trimmedDescription, payee?.Trim(), sourceKind, sourceId, nowUtc);

        var built = BuildPostings(id, postings, accountsById, nowUtc);
        if (built.IsFailure) return built.Error!;

        transaction._postings.AddRange(built.Value);
        return Result<Transaction>.Ok(transaction);
    }

    internal static Result<List<Posting>> BuildPostings(
        Guid transactionId,
        IReadOnlyList<PostingDraft> drafts,
        IReadOnlyDictionary<Guid, Account> accountsById,
        DateTimeOffset nowUtc)
    {
        if (drafts.Count < 2) return DomainErrors.Transaction.TooFewPostings(drafts.Count);

        var seen = new HashSet<Guid>();
        var postings = new List<Posting>(drafts.Count);

        foreach (var draft in drafts)
        {
            if (!accountsById.TryGetValue(draft.AccountId, out var account))
                return DomainErrors.Transaction.AccountUnknown(draft.AccountId);

            if (!seen.Add(draft.AccountId))
                return DomainErrors.Transaction.DuplicateAccount(account.Name);

            if (account.IsArchived) return DomainErrors.Transaction.AccountArchived(account.Name);

            if (draft.Amount.IsZero) return DomainErrors.Transaction.ZeroAmount();

            if (!string.Equals(draft.Amount.Currency.Code, account.CurrencyCode, StringComparison.Ordinal))
                return DomainErrors.Transaction.CurrencyMismatchWithAccount(account.Name);

            postings.Add(new Posting(
                Guid.CreateVersion7(nowUtc), transactionId, draft.AccountId,
                draft.Amount.AmountMinor, draft.Amount.Currency.Code, draft.Memo?.Trim()));
        }

        foreach (var group in postings.GroupBy(p => p.CurrencyCode, StringComparer.Ordinal))
        {
            var residual = group.Sum(p => p.AmountMinor);
            if (residual != 0) return DomainErrors.Transaction.DoesNotBalance(group.Key, residual);
        }

        return Result<List<Posting>>.Ok(postings);
    }

    public Result Void(string reason, DateTimeOffset nowUtc)
    {
        if (IsVoided) return DomainErrors.Transaction.AlreadyVoided();

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return DomainErrors.Transaction.VoidReasonRequired();

        IsVoided = true;
        VoidedAtUtc = nowUtc;
        VoidReason = trimmed;
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    /// <summary>
    /// Replaces the whole content of the transaction. Either every change applies or none does:
    /// the new entries are built and validated before anything is mutated.
    /// </summary>
    public Result Replace(
        DateOnly occurredOn, string description, string? payee,
        IReadOnlyList<PostingDraft> postings,
        IReadOnlyDictionary<Guid, Account> accountsById,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(postings);
        ArgumentNullException.ThrowIfNull(accountsById);

        if (IsVoided) return DomainErrors.Transaction.CannotEditVoided();

        var trimmedDescription = description?.Trim();
        if (string.IsNullOrEmpty(trimmedDescription))
            return DomainErrors.Transaction.DescriptionRequired();

        var built = BuildPostings(Id, postings, accountsById, nowUtc);
        if (built.IsFailure) return Result.Fail(built.Error!);

        OccurredOn = occurredOn;
        Description = trimmedDescription;
        Payee = payee?.Trim();
        _postings.Clear();
        _postings.AddRange(built.Value);
        UpdatedAtUtc = nowUtc;

        return Result.Ok();
    }

    public Result SetExternalRef(string? externalRef, DateTimeOffset nowUtc)
    {
        ExternalRef = string.IsNullOrWhiteSpace(externalRef) ? null : externalRef.Trim();
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    public override string ToString() =>
        $"{OccurredOn:O} {Description} ({_postings.Count} entries){(IsVoided ? " [voided]" : "")}";
}
