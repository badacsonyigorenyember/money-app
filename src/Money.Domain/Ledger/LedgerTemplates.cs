using Money.Domain.Accounts;
using Money.Domain.Primitives;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Ledger;

/// <summary>
/// The four shapes a user actually enters. Each one produces a balanced transaction, and the
/// transfer and opening-balance shapes touch no Kind=Expense account - which is why moving
/// money can never be reported as spending (I12).
/// </summary>
public static class LedgerTemplates
{
    public static Result<Transaction> Expense(
        Guid id, DateOnly occurredOn, string description, string? payee,
        Account paidFrom, Account category, MoneyValue amount, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(paidFrom);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(amount);

        if (category.Kind != AccountKind.Expense || !category.IsCategory)
            return DomainErrors.Account.NotACategory(category.Name);

        if (amount.Sign <= 0) return DomainErrors.Transaction.ZeroAmount();

        return Build(id, occurredOn, description, payee,
                     debit: category, credit: paidFrom, amount, nowUtc);
    }

    public static Result<Transaction> Income(
        Guid id, DateOnly occurredOn, string description, string? payee,
        Account receivedInto, Account category, MoneyValue amount, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(receivedInto);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(amount);

        if (category.Kind != AccountKind.Income || !category.IsCategory)
            return DomainErrors.Account.NotACategory(category.Name);

        if (amount.Sign <= 0) return DomainErrors.Transaction.ZeroAmount();

        return Build(id, occurredOn, description, payee,
                     debit: receivedInto, credit: category, amount, nowUtc);
    }

    public static Result<Transaction> Transfer(
        Guid id, DateOnly occurredOn, string description,
        Account from, Account to, MoneyValue amount, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(amount);

        if (from.Kind != AccountKind.Asset)
            return DomainErrors.Account.KindRoleMismatch(from.Kind.ToString(), from.Role.ToString());
        if (to.Kind != AccountKind.Asset)
            return DomainErrors.Account.KindRoleMismatch(to.Kind.ToString(), to.Role.ToString());

        if (amount.Sign <= 0) return DomainErrors.Transaction.ZeroAmount();

        return Build(id, occurredOn, description, payee: null,
                     debit: to, credit: from, amount, nowUtc);
    }

    public static Result<Transaction> OpeningBalance(
        Guid id, DateOnly occurredOn, Account account, Account openingBalanceEquity,
        MoneyValue amount, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(openingBalanceEquity);
        ArgumentNullException.ThrowIfNull(amount);

        if (account.Kind != AccountKind.Asset)
            return DomainErrors.Account.KindRoleMismatch(account.Kind.ToString(), account.Role.ToString());

        if (openingBalanceEquity.Role != AccountRole.OpeningBalance)
            return DomainErrors.Account.KindRoleMismatch(
                openingBalanceEquity.Kind.ToString(), openingBalanceEquity.Role.ToString());

        if (amount.IsZero) return DomainErrors.Transaction.ZeroAmount();

        return Build(id, occurredOn, "Opening balance", payee: null,
                     debit: account, credit: openingBalanceEquity, amount, nowUtc);
    }

    private static Result<Transaction> Build(
        Guid id, DateOnly occurredOn, string description, string? payee,
        Account debit, Account credit, MoneyValue amount, DateTimeOffset nowUtc)
    {
        var accountsById = new Dictionary<Guid, Account> { [debit.Id] = debit, [credit.Id] = credit };

        return Transaction.Create(
            id, occurredOn, description, payee, TransactionSourceKind.Manual, sourceId: null,
            [new PostingDraft(debit.Id, amount), new PostingDraft(credit.Id, amount.Negate())],
            accountsById, nowUtc);
    }
}
