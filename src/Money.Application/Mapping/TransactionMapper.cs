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
                posting.AccountId, AccountDisplayName.For(account.Name, account.IsDeleted),
                account.Path, account.Kind.ToString(),
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

            // DisplayAmountMapper is the only place a sign is flipped for a human. It is a no-op
            // for the common case (an Expense or Asset headline is already stored debit-positive),
            // and it is what keeps an Income, Liability or Equity headline - stored credit-negative
            // - from reading as a negative number here.
            var amount = DisplayAmountMapper.ToDisplay(row.SignedAmountMinor, row.HeadlineKind, currency);

            return new TransactionListItemDto(
                row.Id, row.OccurredOn, row.Description, row.Payee, row.IsVoided,
                amount, row.CurrencyCode, row.CategoryName, row.AccountName);
        }).ToArray();

        return new TransactionPageDto(items, page.NextCursor);
    }
}
