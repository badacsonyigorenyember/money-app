using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Transactions;

public sealed class CreateTransactionHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        CreateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = await TransactionLineResolver.ResolveAsync(
            accounts, request.Lines, cancellationToken);
        if (resolved.IsFailure) return resolved.Error!;

        var now = clock.UtcNow;
        var created = Transaction.Create(
            Guid.CreateVersion7(now), request.OccurredOn, request.Description ?? "", request.Payee,
            TransactionSourceKind.Manual, sourceId: null,
            resolved.Value.Drafts, resolved.Value.AccountsById, now);

        if (created.IsFailure) return created.Error!;

        transactions.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<TransactionDto>.Ok(
            TransactionMapper.ToDto(created.Value, resolved.Value.AccountsById));
    }
}

/// <summary>
/// Turns display amounts into stored minor units, using each line's own account kind. This is the
/// only sign conversion on the write path (see DisplayAmountMapper).
/// </summary>
internal static class TransactionLineResolver
{
    internal sealed record Resolved(
        IReadOnlyList<PostingDraft> Drafts, IReadOnlyDictionary<Guid, Account> AccountsById);

    public static async Task<Result<Resolved>> ResolveAsync(
        IAccountRepository accounts,
        IReadOnlyList<TransactionLineRequest> lines,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
            return DomainErrors.Transaction.TooFewPostings(0);

        var accountsById = new Dictionary<Guid, Account>();
        var drafts = new List<PostingDraft>(lines.Count);

        foreach (var line in lines)
        {
            if (!accountsById.TryGetValue(line.AccountId, out var account))
            {
                account = await accounts.FindAsync(line.AccountId, cancellationToken);
                if (account is null) return DomainErrors.Transaction.AccountUnknown(line.AccountId);
                accountsById[line.AccountId] = account;
            }

            var currency = Currency.FromCode(account.CurrencyCode);
            if (currency.IsFailure) return currency.Error!;

            var minor = DisplayAmountMapper.ToStored(line.Amount, account.Kind, currency.Value);
            drafts.Add(new PostingDraft(line.AccountId, MoneyValue.Of(minor, currency.Value), line.Memo));
        }

        return Result<Resolved>.Ok(new Resolved(drafts, accountsById));
    }
}
