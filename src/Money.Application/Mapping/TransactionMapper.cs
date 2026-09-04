using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;

namespace Money.Application.Mapping;

public static class TransactionMapper
{
    public static TransactionDto ToDto(
        Transaction transaction, IReadOnlyDictionary<Guid, Account> accountsById)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(accountsById);

        var lines = transaction.Postings.Select(posting =>
        {
            var account = accountsById[posting.AccountId];
            var currency = Currency.FromCode(posting.CurrencyCode).Value;

            return new TransactionLineDto(
                posting.AccountId, account.Name, account.Path, account.Kind.ToString(),
                DisplayAmountMapper.ToDisplay(posting.AmountMinor, account.Kind, currency),
                posting.CurrencyCode, posting.Memo);
        }).ToArray();

        return new TransactionDto(
            transaction.Id, transaction.OccurredOn, transaction.Description, transaction.Payee,
            transaction.SourceKind.ToString(), transaction.IsVoided, transaction.VoidReason, lines);
    }

    public static TransactionPageDto ToPageDto(TransactionPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var items = page.Rows.Select(row =>
        {
            var currency = Currency.FromCode(row.CurrencyCode).Value;

            // The headline amount is the expense line when there is one, which is already a debit,
            // so it needs no orientation flip - the query picked a Kind=Expense row.
            return new TransactionListItemDto(
                row.Id, row.OccurredOn, row.Description, row.Payee, row.IsVoided,
                row.SignedAmountMinor / currency.MinorUnitScale, row.CurrencyCode,
                row.CategoryName, row.AccountName);
        }).ToArray();

        return new TransactionPageDto(items, page.NextCursor);
    }
}
