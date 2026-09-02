using CsCheck;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.TestSupport;

/// <summary>
/// Generates realistic ledgers: opening balances, salary, everyday spending, transfers into a
/// savings pocket, investment purchases, and some voided transactions. The mix matters - a
/// generator that only makes expenses cannot falsify I12.
/// </summary>
public static class LedgerGen
{
    public sealed record GeneratedLedger(
        IReadOnlyList<Account> Accounts,
        IReadOnlyList<Transaction> Transactions,
        IReadOnlyDictionary<Guid, Account> AccountsById,
        Account Bank,
        Account Savings,
        Account Investment,
        Account Salary,
        Account Groceries,
        Account Fun,
        Account OpeningEquity);

    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Gen<DateOnly> AnyDate =
        Gen.Int[new DateOnly(2026, 1, 1).DayNumber, new DateOnly(2026, 12, 31).DayNumber]
           .Select(DateOnly.FromDayNumber);

    private static readonly Gen<long> AnyAmount = Gen.Long[1, 500_000];

    private enum Shape { Expense, Income, TransferToSavings, InvestmentPurchase }

    private static readonly Gen<Shape> AnyShape = Gen.OneOfConst(
        Shape.Expense, Shape.Expense, Shape.Expense,   // expenses are the common case
        Shape.Income, Shape.TransferToSavings, Shape.InvestmentPurchase);

    /// <summary>
    /// Ledgers of 0 to 60 transactions. The count is drawn first, then four parallel arrays of
    /// that length, so every transaction has a shape, an amount, a date and a voided flag.
    /// </summary>
    public static Gen<GeneratedLedger> Ledgers { get; } =
        Gen.Int[0, 60].SelectMany(count =>
            Gen.Select(AnyShape.Array[count, count],
                       AnyAmount.Array[count, count],
                       AnyDate.Array[count, count],
                       Gen.Bool.Array[count, count])
               .Select(t => Assemble(t.Item1, t.Item2, t.Item3, t.Item4)));

    private static GeneratedLedger Assemble(
        Shape[] shapes, long[] amounts, DateOnly[] dates, bool[] voided)
    {
        var bank = Root("Current", AccountKind.Asset, AccountRole.Bank);
        var savings = Root("Rainy day", AccountKind.Asset, AccountRole.SavingsPocket);
        var investment = Root("Term deposit", AccountKind.Asset, AccountRole.Investment);
        var salary = Root("Salary", AccountKind.Income, AccountRole.Category);
        var groceries = Root("Groceries", AccountKind.Expense, AccountRole.Category);
        var fun = Root("Fun", AccountKind.Expense, AccountRole.Category);
        var opening = Root("Opening balance", AccountKind.Equity, AccountRole.OpeningBalance);

        var accounts = new[] { bank, savings, investment, salary, groceries, fun, opening };
        var byId = accounts.ToDictionary(a => a.Id);
        var transactions = new List<Transaction>();

        transactions.Add(Build2(new DateOnly(2026, 1, 1), "Opening balance",
            bank, 10_000_000, opening, -10_000_000, byId));

        for (var i = 0; i < shapes.Length; i++)
        {
            var amount = amounts[i];
            var date = dates[i];

            var transaction = shapes[i] switch
            {
                Shape.Expense => Build2(date, "Spend",
                    i % 2 == 0 ? groceries : fun, amount, bank, -amount, byId),
                Shape.Income => Build2(date, "Salary", bank, amount, salary, -amount, byId),
                Shape.TransferToSavings => Build2(date, "To savings",
                    savings, amount, bank, -amount, byId),
                Shape.InvestmentPurchase => Build2(date, "Buy deposit",
                    investment, amount, bank, -amount, byId),
                _ => throw new NotSupportedException()
            };

            if (voided[i]) transaction.Void("Generated void", Origin);

            transactions.Add(transaction);
        }

        return new GeneratedLedger(accounts, transactions, byId,
                                   bank, savings, investment, salary, groceries, fun, opening);

        static Account Root(string name, AccountKind kind, AccountRole role) =>
            Account.Create(Guid.CreateVersion7(Origin), name, kind, role, null, Currency.Eur, Origin).Value;
    }

    private static Transaction Build2(
        DateOnly on, string description,
        Account debit, long debitMinor, Account credit, long creditMinor,
        IReadOnlyDictionary<Guid, Account> byId) =>
        Transaction.Create(
            Guid.CreateVersion7(Origin), on, description, null,
            TransactionSourceKind.Manual, null,
            [
                new PostingDraft(debit.Id, MoneyValue.Of(debitMinor, Currency.Eur)),
                new PostingDraft(credit.Id, MoneyValue.Of(creditMinor, Currency.Eur))
            ],
            byId, Origin).Value;
}
