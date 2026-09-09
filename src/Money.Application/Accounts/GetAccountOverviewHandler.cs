using System.Globalization;
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Money;
using Money.Domain.Periods;
using Money.Domain.Time;

namespace Money.Application.Accounts;

/// <summary>
/// The home page's "how am I doing" read model, for one month: where each account started the
/// month, where it stands on every day since, and what came in and went out along the way.
///
/// The month comes from <see cref="PeriodResolver"/>, so a ledger anchored to payday reports on
/// payday months rather than calendar ones without a line changing here.
/// </summary>
public sealed class GetAccountOverviewHandler(
    IAccountRepository accounts,
    ILedgerQueries queries,
    ISettingsRepository settings,
    IExchangeRates rates,
    IClock clock)
{
    /// <param name="periodKey">
    /// A key from <see cref="PeriodKey.ToKeyString"/>, or null for the month in progress. An
    /// unparsable one falls back to the month in progress rather than failing: it arrives from a
    /// URL, and a URL a user has edited by hand should land somewhere sensible.
    /// </param>
    public async Task<MonthOverviewDto> HandleAsync(
        string? periodKey = null, CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetAsync(cancellationToken);
        var resolver = new PeriodResolver(stored?.PeriodDefinition ?? PeriodDefinition.Default);
        var baseCurrency = stored?.BaseCurrencyCode ?? "EUR";

        var today = resolver.TodayIn(clock);
        var current = resolver.Resolve(today, PeriodType.Monthly);

        var key = PeriodKey.Parse(periodKey ?? "") is { IsSuccess: true } parsed
                  && parsed.Value.Type == PeriodType.Monthly
            ? parsed.Value
            : current;

        var range = resolver.Range(key);
        var lastDay = range.EndExclusive.AddDays(-1);

        // A month in progress stops at today. Anything later has not happened, and a line drawn
        // flat across it would claim a balance the ledger has not got.
        var drawnTo = today < lastDay ? today : lastDay;

        // The same set the Accounts page shows: what a person calls an account. Equity and
        // Adjustment are bookkeeping and never appear.
        var open = (await accounts.ListAsync(AccountKind.Asset, null, false, cancellationToken))
            .Where(a => a.Role is AccountRole.Bank or AccountRole.Cash
                                  or AccountRole.SavingsPocket or AccountRole.Investment)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name, StringComparer.CurrentCulture)
            .ToArray();

        var flows = open.Length == 0
            ? []
            : await queries.FlowsAsync(range.Start, range.EndExclusive, cancellationToken);

        var moves = open.Length == 0
            ? []
            : await queries.DailyNetAsync(range.Start, range.EndExclusive, cancellationToken);

        var panels = new List<AccountMonthDto>(open.Length);

        foreach (var account in open)
        {
            var currency = Currency.FromCode(account.CurrencyCode);

            decimal ToDisplay(long minor, AccountKind kind) => currency.IsSuccess
                ? DisplayAmountMapper.ToDisplay(minor, kind, currency.Value)
                : 0m;

            // What the month was handed. Asked of the ledger rather than derived from today's
            // balance, so a future-dated entry cannot bend the line backwards.
            var openingMinor = await queries.BalanceOfAsync(
                account.Id, range.Start.AddDays(-1), cancellationToken);

            var byDay = moves
                .Where(m => m.AccountId == account.Id)
                .ToDictionary(m => m.OccurredOn, m => m.DeltaMinor);

            var running = openingMinor;
            var days = new List<DayBalanceDto>();

            for (var day = range.Start; day <= drawnTo; day = day.AddDays(1))
            {
                running += byDay.GetValueOrDefault(day);
                days.Add(new DayBalanceDto(day, ToDisplay(running, account.Kind)));
            }

            var flow = flows.FirstOrDefault(f => f.AccountId == account.Id);

            // Income legs are stored negative and expense legs positive; the mapper is the one
            // place that turns both into the positive numbers a reader expects.
            var moneyIn = ToDisplay(flow?.IncomeMinor ?? 0L, AccountKind.Income);
            var moneyOut = ToDisplay(flow?.ExpenseMinor ?? 0L, AccountKind.Expense);

            // Display only, and asked for once per account: an account kept in another currency
            // still has to land somewhere on a chart drawn in the base one. A null here means no
            // rate could be had, and the chart leaves the account off rather than guessing.
            var rateToBase = await rates.RateAsync(
                account.CurrencyCode, baseCurrency, cancellationToken);

            panels.Add(new AccountMonthDto(
                account.Id, account.Name, account.CurrencyCode,
                ToDisplay(openingMinor, account.Kind),
                days.Count > 0 ? days[^1].Balance : ToDisplay(openingMinor, account.Kind),
                moneyIn, moneyOut, moneyIn - moneyOut,
                days, rateToBase));
        }

        return new MonthOverviewDto(
            key.ToKeyString(), MonthLabel(key), range.Start, lastDay,
            lastDay.DayNumber - range.Start.DayNumber + 1,
            baseCurrency,
            resolver.Previous(key).ToKeyString(),
            key == current ? null : resolver.Next(key).ToKeyString(),
            panels);
    }

    /// <summary>
    /// A month is named after the calendar month its period opens in, which is exactly what the
    /// key's index already is - so a payday month running 25 Aug to 24 Sep reads as "August", the
    /// same month the settings page says the period starts in.
    /// </summary>
    private static string MonthLabel(PeriodKey key) =>
        $"{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(key.Index)} {key.Year}";
}
