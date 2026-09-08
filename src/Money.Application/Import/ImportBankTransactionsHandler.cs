using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Import;

/// <summary>
/// Pulls a window of booked lines from a bank feed into the ledger.
///
/// A feed knows the amount and the counterparty's name but not what the money was for, so every
/// imported line is booked against an Unclassified category and recategorised later - which is
/// only ever a matter of repointing that one posting. The external reference is what makes a
/// re-run a no-op; the unique index behind it is what makes that true even if this pre-check
/// races.
/// </summary>
public sealed class ImportBankTransactionsHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IBankFeed feed,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public const int DefaultWindowDays = 35;
    public const int MaxWindowDays = 365;

    private const string UnclassifiedExpensePath = "/expense/unclassified";
    private const string UnclassifiedIncomePath = "/income/unclassified";

    public async Task<Result<ImportResultDto>> HandleAsync(
        ImportBankTransactionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var windowDays = request.WindowDays ?? DefaultWindowDays;
        if (windowDays is < 1 or > MaxWindowDays)
            return DomainErrors.Import.WindowOutOfRange(windowDays, MaxWindowDays);

        var bankAccount = await accounts.FindAsync(request.AccountId, cancellationToken);
        if (bankAccount is null) return DomainErrors.Account.NotFound(request.AccountId);

        if (bankAccount.Kind != AccountKind.Asset)
            return DomainErrors.Account.KindRoleMismatch(
                bankAccount.Kind.ToString(), bankAccount.Role.ToString());

        var currency = Currency.FromCode(bankAccount.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var to = await TodayResolver.TodayAsync(settings, clock, cancellationToken);
        var from = to.AddDays(-(windowDays - 1));

        var fetched = await feed.FetchBookedAsync(from, to, cancellationToken);
        if (fetched.IsFailure) return fetched.Error!;

        // Deduplicate the batch against itself before the ledger: the feed's own reference is
        // meant to be unique, but the client's hash fallback (for an ASPSP that supplies none)
        // can collide, and a duplicate inside one SaveChanges would fail the whole import on the
        // unique index rather than just that line.
        var lines = fetched.Value
            .DistinctBy(line => line.ExternalRef, StringComparer.Ordinal)
            .ToArray();

        var inCurrency = lines
            .Where(line => string.Equals(line.CurrencyCode, bankAccount.CurrencyCode, StringComparison.Ordinal))
            .ToArray();

        var alreadyPresent = await transactions.ExistingExternalRefsAsync(
            inCurrency.Select(line => line.ExternalRef).ToArray(), cancellationToken);

        var known = new HashSet<string>(alreadyPresent, StringComparer.Ordinal);
        var toImport = inCurrency.Where(line => !known.Contains(line.ExternalRef)).ToArray();

        var counterparties = toImport.Length == 0
            ? Result<(Account Expense, Account Income)>.Ok((null!, null!))
            : await ResolveUnclassifiedAsync(currency.Value, cancellationToken);
        if (counterparties.IsFailure) return counterparties.Error!;

        var now = clock.UtcNow;
        foreach (var line in toImport)
        {
            var built = LedgerTemplates.Imported(
                Guid.CreateVersion7(now), line.BookedOn, line.Description, line.Payee,
                bankAccount, counterparties.Value.Expense, counterparties.Value.Income,
                MoneyValue.Of(line.AmountMinor, currency.Value), line.ExternalRef, now);

            // A build failure here is a configuration problem (an archived Unclassified category,
            // a blank description from the feed) that applies to the whole batch, not to one
            // line. Fail the import so nothing is half-applied, rather than silently dropping it.
            if (built.IsFailure) return built.Error!;

            transactions.Add(built.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ImportResultDto>.Ok(new ImportResultDto(
            From: from,
            To: to,
            Fetched: lines.Length,
            Imported: toImport.Length,
            AlreadyPresent: known.Count,
            SkippedForeignCurrency: lines.Length - inCurrency.Length));
    }

    /// <summary>
    /// Finds, or creates on first import, the two categories every imported line lands on.
    ///
    /// ponytail: one Unclassified pair, in the imported account's currency. Importing a second
    /// account in a different currency will fail with transaction.currency_mismatch_with_account;
    /// give the pair per-currency paths if that ever happens.
    /// </summary>
    private async Task<Result<(Account Expense, Account Income)>> ResolveUnclassifiedAsync(
        Currency currency, CancellationToken cancellationToken)
    {
        var expense = await GetOrCreateAsync(
            UnclassifiedExpensePath, "Unclassified", AccountKind.Expense, currency, cancellationToken);
        if (expense.IsFailure) return expense.Error!;

        var income = await GetOrCreateAsync(
            UnclassifiedIncomePath, "Unclassified", AccountKind.Income, currency, cancellationToken);
        if (income.IsFailure) return income.Error!;

        return Result<(Account, Account)>.Ok((expense.Value, income.Value));
    }

    private async Task<Result<Account>> GetOrCreateAsync(
        string path, string name, AccountKind kind, Currency currency,
        CancellationToken cancellationToken)
    {
        var existing = await accounts.FindByPathAsync(path, cancellationToken);
        if (existing is not null) return Result<Account>.Ok(existing);

        var now = clock.UtcNow;
        var created = Account.Create(
            Guid.CreateVersion7(now), name, kind, AccountRole.Category, null, currency, now);
        if (created.IsFailure) return created.Error!;

        accounts.Add(created.Value);
        return created;
    }
}
