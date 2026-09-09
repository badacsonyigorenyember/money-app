# Money Tracker — Design Specification

- **Date:** 2026-09-01
- **Status:** Approved design, pending implementation plan
- **Target:** Windows 11 desktop executable, single user, offline-first
- **Future target:** Same binary as a self-hosted server (Docker), reachable from a phone

---

## 1. Purpose

A personal income, spending, savings and investment tracker that runs as a
single local executable on Windows, stores its data in one SQLite file, and
can later be run as a LAN/self-hosted server without rewriting the
application.

The user is the only user. There is no cloud service, no telemetry, and no
bank connection. Data never leaves the machine unless the user exports it.

### Success criteria

1. Recording a normal expense takes under five seconds.
2. Transfers between own accounts, savings contributions and investment
   purchases never appear as spending in any report.
3. Every displayed balance is derivable from the ledger and reconcilable
   against a real bank statement.
4. The dashboard answers "how much have I spent this month, and am I over
   budget" without interaction.
5. The whole database can be exported and restored from the UI.
6. Switching from desktop mode to server mode is a configuration change, not
   a code change.

---

## 2. Non-goals (v1)

Recorded explicitly so scope creep is visible:

| Excluded | Reason | Left room for it? |
|---|---|---|
| Market-priced holdings (stocks, ETFs, crypto) | User tracks fixed-rate instruments only | Yes — account role and valuation table shape reserved |
| Bank / CSV statement import | **Shipped after approval, outside the original v1 line.** Enable Banking (PSD2 AISP) read-only feed for one linked account; CSV import still deferred | `Transaction.SourceKind = Import` + `ExternalRef`, guarded by `UX_Transactions_Import_ExternalRef` |
| Multi-currency UI | Single currency in practice | Yes — currency code stored on every amount from day one |
| Credit cards / loans UI | Not requested | Yes — `AccountKind.Liability` exists in the schema |
| Multi-user / accounts / login | Single-user desktop app | No. The `ICurrentUser` seam was removed on 9 September 2026; there are still no owner columns to add one back around. |
| Mobile native app | Not wanted; this is a desktop app | No. The REST API, OpenAPI document and PWA manifest that were the affordance were removed on 9 September 2026. |
| Receipt attachments, OCR | Not requested | No |

---

## 3. Decision log

### Decisions taken by the user

| # | Decision | Chosen | Rejected |
|---|---|---|---|
| D1 | Ledger model | **Double-entry** | Flat signed-amount transaction list |
| D2 | Savings pockets | **Both real and virtual, modelled distinctly** | Virtual-only (YNAB style); real-only |
| D3 | Investment types | **Fixed-rate / interest-bearing only** | Market-priced holdings; recurring contributions |
| D4 | Period definition | **Configurable, defaulting to calendar month** | Hardcoded calendar month; payday-anchored only |

### Decisions taken by the implementer

| # | Decision | Rationale |
|---|---|---|
| D5 | Categories are expense/income **accounts** in the same tree, not a separate tagging dimension | One mechanism gives free hierarchy, free subtree rollups, and budgets that are simply caps on a subtree. A separate `CategoryId` column forces every report to special-case it and requires hand-rolled hierarchy. |
| D6 | The product is a **web application hosted in a WebView2 window**, not a native desktop app | Makes the future server/phone mode a hosting flag rather than a rewrite. WebView2 ships with Windows 11, so the cost today is zero. |
| D7 | **Mutation testing (Stryker.NET) gates the Domain project** | Line coverage is a poor signal for money logic. Mutation score is the only measure that proves the assertions are real. |
| D8 | Money is `long` **minor units** plus an ISO-4217 code; rates are `decimal` | Floating point on money is the classic fatal bug in this category of app. |
| D9 | Amounts stored as SQLite `INTEGER`; rates as `NUMERIC`/`TEXT` | Never `REAL`. |
| D10 | Database lives in `%APPDATA%`, **not** next to the executable | The project folder is on the Desktop, which is commonly OneDrive-synced. A cloud-synced SQLite file in WAL mode is a corruption risk. |
| D11 | Transactions are **voided, never deleted**; reference data is archived, and deleting an archived account or category never touches its transactions | Preserves an audit trail and keeps historical reports intact. Deleting erases the rows outright when nothing refers to them, and otherwise flags them `IsDeleted` so history can still name them. |
| D12 | ~~`ICurrentUser` seam now~~, and **no `OwnerId` columns** | The seam was an interface with one implementation nobody resolved, and went on 9 September 2026 with the rest of the server-mode scaffolding. The half that mattered stands: adding one column to a single-user database later is trivial; speculative multi-tenancy is not. |

---

## 4. Technology

| Layer | Choice |
|---|---|
| Language / runtime | C# / .NET 9 |
| API | none - the window calls Razor page handlers directly |
| UI | Razor Pages + HTMX + vendored Chart.js (no Node build step) |
| Persistence | EF Core 9 + SQLite (WAL) |
| Desktop shell | WebView2 host process starting Kestrel on loopback |
| Tests | xUnit, FluentAssertions, CsCheck (property tests), NetArchTest, Stryker.NET |
| CI | GitHub Actions |

### Why this stack

- **Correctness first.** `decimal`, records, non-nullable reference types and
  exhaustive switches make the money and period logic hard to get wrong.
- **One toolchain.** Only the .NET SDK is required. No Node, no Rust, no
  Python. The developer machine currently has none of these installed, so
  every option costs an install; this one costs the fewest.
- **One binary, two hosting shapes.** Self-contained single-file `win-x64`
  publish for the desktop; `linux-x64` container from the same source for the
  server phase.
- **No frontend build step.** HTMX plus a vendored Chart.js reaches roughly
  90% of the desired UX at a fraction of the moving parts, and works on a
  phone browser without change. Because the API boundary is real, the UI can
  later be replaced with React or a native client without touching the domain.

Rejected alternatives: **Electron/Tauri** (second toolchain, heavier
packaging, no benefit when WebView2 is preinstalled); **Python/FastAPI**
(fastest to write, weakest single-executable story); **React SPA** (requires
Node purely as a build step for a solo project).

---

## 5. Domain model

### 5.1 Money and currency

```
Money       = (long AmountMinor, Currency Currency)
Currency    = ISO-4217 alpha code + minor-unit exponent
```

Rules:

- Arithmetic between different currencies throws.
- No implicit conversion to or from floating-point types anywhere.
- Division and percentage application return an exact `decimal` intermediate
  and round only at the point of posting (see 5.7 rounding).

### 5.2 Accounts

```
AccountKind = Asset | Liability | Income | Expense | Equity
AccountRole = Bank | Cash | SavingsPocket | Investment | Category | OpeningBalance | Adjustment
```

| Field | Type | Notes |
|---|---|---|
| `Id` | GUID v7 | Time-ordered for index locality |
| `Name` | string(120) | Unique among siblings |
| `Kind` | AccountKind | |
| `Role` | AccountRole | |
| `ParentAccountId` | GUID? | Hierarchy |
| `Path` | string | Materialised path (`/expense/gaming/steam`) for subtree queries |
| `CurrencyCode` | char(3) | |
| `IsArchived` | bool | Archive, never delete |
| `OpenedOn` | date? | |
| `SortOrder`, `ColorHex`, `Icon`, `Notes` | | Presentation |

Consistency constraints (enforced in the domain **and** as DB check
constraints):

- `Role = Category` implies `Kind` is `Income` or `Expense`
- `Role` in `{SavingsPocket, Investment, Bank, Cash}` implies `Kind = Asset`
- `Role` in `{OpeningBalance, Adjustment}` implies `Kind = Equity`
- A parent and child must share the same `Kind`.
- `Path` is recomputed for the whole subtree on reparent.

**Categories are accounts.** "Alcohol", "Gaming", "Gaming → Steam" are
`Kind=Expense, Role=Category` accounts. The UI exposes them as *Categories*
in a dropdown filtered to that role, and never uses accounting vocabulary.

### 5.3 Transactions and postings

```
Transaction
  Id, OccurredOn (date), BookedAtUtc (timestamp),
  Description, Payee?,
  SourceKind = Manual | Recurring | Accrual | Import,
  SourceId?, ExternalRef?,
  IsVoided, VoidedAtUtc?, VoidReason?,
  CreatedAtUtc, UpdatedAtUtc

Posting
  Id, TransactionId, AccountId,
  AmountMinor (long, signed), CurrencyCode, Memo?
```

**Sign convention (fixed, documented once, tested hard):**

> **Positive = debit. Negative = credit.**
> Asset and Expense accounts increase with a positive amount.
> Income, Liability and Equity accounts increase with a negative amount.

Income accounts therefore carry a negative internal balance; the presentation
layer negates them for display. This is the single place where the sign
convention leaks, and it is confined to one mapper.

**Core invariant:** for every non-voided transaction, postings sum to zero
per currency, and a transaction has at least two postings.

Worked examples:

| Event | Postings |
|---|---|
| Salary 3000 into current account | Bank `+300000`, Income:Salary `-300000` |
| Dinner 20 from current account | Expense:Food `+2000`, Bank `-2000` |
| Transfer 500 to savings pocket | Asset:SavingsPocket `+50000`, Bank `-50000` |
| Interest 12.34 capitalised | Asset:Investment `+1234`, Income:Interest `-1234` |
| Opening balance 1000 | Bank `+100000`, Equity:OpeningBalance `-100000` |
| Split: 50 groceries + 10 alcohol on one card payment | Expense:Groceries `+5000`, Expense:Alcohol `+1000`, Bank `-6000` |

Note that the transfer and the investment rows touch no `Role=Category`
account. That is precisely why they can never be counted as spending: every
spending report is a query restricted to `Kind=Expense`.

**Balances** are always `SUM(AmountMinor)` over non-voided postings, never a
stored field. `BalanceAsOf(accountId, date)` adds a date predicate;
`SubtreeBalance` joins on `Path LIKE prefix || '%'`.

### 5.4 Periods

```
PeriodType       = Weekly | Monthly | Quarterly | Yearly
PeriodDefinition = (Anchor, TimeZoneId)
Anchor           = CalendarMonth | DayOfMonth(n)   where 1 <= n <= 28
PeriodKey        = (PeriodType, Year, Index)   e.g. 2026-M09, 2026-W36, 2026-Y
```

`DayOfMonth` is restricted to 1–28 so that every month has the anchor day and
no clamping ambiguity exists at period boundaries. Values above 28 are
rejected with a validation error explaining why.

A period anchored on day *n* is labelled by the month in which it **starts**:
the period from 25 September to 24 October is `2026-M09`.

`PeriodResolver` is the only component that computes period boundaries:

```
PeriodKey    Resolve(DateOnly date, PeriodType type)
DateRange    Range(PeriodKey key)          // [start, endExclusive)
PeriodKey    Next(PeriodKey key) / Previous(PeriodKey key)
```

All time arithmetic happens in the configured IANA time zone and is then
converted to UTC for storage. Nothing outside `PeriodResolver` computes month
boundaries. This is the seam that makes payday-anchoring a setting change.

### 5.5 Budgets

A budget is a **cap on a category subtree for a period**. It moves no money.

| Field | Type |
|---|---|
| `Id` | GUID |
| `CategoryAccountId` | GUID — subtree root |
| `PeriodType` | PeriodType |
| `LimitMinor` | long |
| `StartPeriod` | PeriodKey |
| `EndPeriod` | PeriodKey? |
| `RolloverPolicy` | `None` or `Carry` |
| `IsActive` | bool |

Nothing about "spent" is stored. `BudgetStatus(budget, periodKey)` is
computed:

```
window    = PeriodResolver.Range(periodKey)
spent     = SUM(postings) where account.Path startsWith category.Path
                            and account.Kind = Expense
                            and transaction.OccurredOn in window
                            and not transaction.IsVoided
carriedIn = RolloverPolicy = Carry
              ? max(0, previous period's remaining)   // recursive, capped at StartPeriod
              : 0
limit     = LimitMinor + carriedIn
remaining = limit - spent
state     = spent <= 0.8*limit ? OnTrack
          : spent <= limit     ? Warning
          :                      Exceeded
```

Overlap rule: two active budgets may not target the same account or an
ancestor/descendant pair for the same `PeriodType`. Validated on create and
update.

### 5.6 Pockets

One user-facing concept, two honest storage shapes.

```
PocketKind = Real | Virtual

Pocket
  Id, Name, Kind, CurrencyCode,
  TargetMinor?, TargetDate?,
  BackingAccountId,        // Real: the Asset/SavingsPocket account
                           // Virtual: the Asset/Bank or Cash account it draws from
  IsArchived, Notes
```

**Real pocket.** Backed by an `Asset/SavingsPocket` account. Funding creates a
real transfer transaction, so the app's balance matches the bank's. Balance =
account balance.

**Virtual pocket.** Backed by allocation rows over money already held:

```
PocketAllocation
  Id, PocketId, PeriodKey, AmountMinor, OccurredOn, Memo
```

Balance = sum of allocations (withdrawals are negative allocations).

**Virtual over-allocation invariant:** for any backing account,

```
SUM(virtual pocket allocations against that account) <= account balance
```

Violating writes are rejected with a domain error naming the shortfall. A
`GET /pockets/unallocated` endpoint reports the free remainder.

Both kinds render identically: name, current, target, progress bar, and
projected completion date when `TargetDate` is set.

### 5.7 Investments (fixed-rate)

Backed by an `Asset/Investment` account plus terms:

```
InvestmentTerms
  Id, AccountId,
  PrincipalMinor, AnnualRatePercent (decimal),
  CompoundingFrequency = Daily | Monthly | Quarterly | Annually | AtMaturity,
  DayCount             = Act365 | Act360 | Thirty360,
  StartDate, MaturityDate?,
  InterestHandling     = Capitalise | PayOutTo(accountId),
  WithholdingTaxPercent (decimal, default 0),
  IsClosed
```

Two engines, strictly separated:

**AccrualEngine — writes real ledger transactions.**

```
IEnumerable<Accrual> Accrue(InvestmentTerms terms, DateOnly from, DateOnly to)
```

- Accrual dates are the period ends implied by `CompoundingFrequency`;
  `AtMaturity` produces a single simple-interest accrual on `MaturityDate`.
- For each accrual date the engine maintains an exact `decimal` running total
  `accruedExact` and posts
  `postAmount = Round(accruedExact) - alreadyPostedTotal`.
  Carrying the remainder this way means the posted total can never drift from
  the exact total by more than one minor unit, regardless of term length.
- Rounding is half-away-from-zero to the currency's minor unit.
- Withholding tax, if non-zero, posts a third leg to `Expense:Tax`.
- `Capitalise` posts `Asset:Investment +x, Income:Interest -x`.
  `PayOutTo(acct)` posts `Asset:<acct> +x, Income:Interest -x`.
- Accruals are idempotent: unique index on
  `(SourceKind='Accrual', SourceId=TermsId, OccurredOn)`. Re-running the
  catch-up job posts nothing new.

**ProjectionEngine — read-only, never touches the ledger.**

```
ProjectionSchedule Project(InvestmentTerms terms, DateOnly horizon)
```

Returns the future balance curve and maturity value for display only.
Projected interest is never written as a transaction, so net worth always
reflects money that actually exists.

### 5.8 Recurring rules

```
RecurringRule
  Id, Name, IsActive,
  TemplateDescription, TemplatePayee?,
  TemplatePostings: [(AccountId, AmountMinor, Memo)],
  Schedule, StartDate, EndDate?, MaxOccurrences?,
  PostingMode = AutoPost | NeedsConfirmation,
  LastMaterialisedThrough (date?)

Schedule
  Frequency = Daily | Weekly | Monthly | Yearly | Custom
  Interval  >= 1
  Weekly  -> DayOfWeek
  Monthly -> DayOfMonth (1..31, clamped to month length)
           | (WeekOfMonth, DayOfWeek)   1..4, or -1 for the last
  Yearly  -> Month + either of the two Monthly shapes
  Custom  -> (Years, Months, Days) applied together; at least one non-zero
```

`ScheduleExpander.Expand(schedule, from, to)` is a pure function returning
occurrence dates. A monthly rule on day 31 yields 28 or 29 February — this is
an explicit test case, as are 30-day months and leap years.

**Materialisation.** A `RecurringMaterialiser` runs at application startup and
whenever the transactions screen is opened, expanding every active rule up to
today:

- `AutoPost` rules create the transaction directly.
- `NeedsConfirmation` rules create a `PendingOccurrence` row that the user
  confirms, edits (variable bills) or skips.

*As built:* every rule is `AutoPost`. `PendingOccurrence`, `MaxOccurrences` and
the confirmation inbox are not implemented, and `TemplatePostings` is stored as
the two account ids the rule posts to — worked out once, at creation, by the
same `LedgerTemplates` a hand-typed entry goes through.

**Idempotency.** Unique index on `(RuleId, OccurrenceDate)` across both
generated transactions and pending occurrences. Running the materialiser
twice, or on two overlapping startups, cannot double-post rent. This is the
single most important safety property of the feature.

Editing a rule never rewrites already-materialised history.

### 5.9 Settings

Single-row table: base currency, `PeriodDefinition` (anchor + time zone),
first day of week, number format, dashboard preferences, backup retention
count, and the desktop window's last size and position.

---

## 6. Invariants (the tested contract)

| # | Invariant |
|---|---|
| I1 | Every non-voided transaction's postings sum to zero, per currency |
| I2 | Every transaction has at least two postings |
| I3 | An account's balance equals the sum of its non-voided postings |
| I4 | Postings referencing an archived account cannot be created |
| I5 | A category's `Kind` matches its parent's `Kind` |
| I6 | Virtual pocket allocations against an account never exceed its balance |
| I7 | Accrual totals match closed-form compound interest to within one minor unit |
| I8 | Schedule expansion is idempotent for a given `(rule, window)` |
| I9 | Periods of one `PeriodType` tile the timeline: no gaps, no overlaps |
| I10 | No two active budgets target overlapping subtrees for the same `PeriodType` |
| I11 | A voided transaction contributes to no balance, report or budget |
| I12 | Spending reports include only `Kind=Expense` accounts, so transfers, pocket funding and investment purchases are structurally excluded |

I1, I3, I7, I8, I9 and I12 are property tests, not example tests.

---

## 7. Architecture

```
src/
  Money.Domain/          entities, value objects, invariants, pure engines
                         (Money, Currency, PeriodKey, PeriodResolver,
                          Accounts, Ledger, Budgets, Pockets,
                          Investments/AccrualEngine, Investments/ProjectionEngine,
                          Recurrence/ScheduleExpander)
                         -> zero external dependencies
  Money.Application/     use cases (commands + queries), ports, validation, DTOs
  Money.Infrastructure/  EF Core DbContext, migrations, repositories,
                         SystemClock, backup, export
  Money.Api/             Razor Pages + HTMX views, page handlers,
                         Problem Details, composition root
  Money.Desktop/         WebView2 host: boots Kestrel on loopback, opens window
tests/
  Money.Domain.Tests/          fast unit + property tests, no I/O
  Money.Application.Tests/     use cases against real in-memory SQLite
  Money.Api.Tests/             WebApplicationFactory integration tests
  Money.Architecture.Tests/    NetArchTest rules
```

### Dependency rule

Dependencies point inward only:
`Desktop -> Api -> Infrastructure -> Application -> Domain`.

Enforced by `Money.Architecture.Tests`, which fails the build if:

- `Money.Domain` references any other project or any third-party package
- `Money.Application` references `Money.Infrastructure` or `Money.Api`
- `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` or
  `DateOnly.FromDateTime(DateTime.Now)` appear outside the composition root
- `double` or `float` appear anywhere in `Money.Domain`
- any public domain type exposes a mutable collection

### Cross-cutting

- **Time.** `IClock` is injected everywhere. There is no ambient time.
- **Identity.** None. One machine, one file, no login, no `OwnerId` columns.
- **Validation.** Domain constructors reject invalid states; the application
  layer returns `Result<T>`, and the page handler that asked puts the error
  message on the screen. Problem Details stays registered for what the
  framework raises on its own, such as a failed antiforgery check.
- **Errors.** A `Result<T>` type for expected failures (validation,
  over-allocation, budget overlap); exceptions only for programmer error.

---

## 8. Persistence

### Location and mode

- Default path: `%APPDATA%/MoneyApp/money.db`, overridable via
  `MONEYAPP_DATA_DIR`.
- `journal_mode=WAL`, `foreign_keys=ON`, `busy_timeout=5000`.
- **Not** stored beside the executable. The project folder is on the Desktop,
  which is frequently OneDrive-synced, and a synced SQLite file in WAL mode
  can be corrupted by the sync client.

### Schema notes

- All amounts: `INTEGER` minor units. All rates and percentages: `NUMERIC`.
  `REAL` is never used.
- Every table has `CreatedAtUtc` and `UpdatedAtUtc`.
- Reference data (accounts, categories, pockets, budgets, rules) is archived
  via `IsArchived`, never deleted.
- Transactions are voided via `IsVoided` + `VoidReason`, never deleted.

### Key indexes and constraints

| Table | Index / constraint |
|---|---|
| `postings` | `(AccountId, TransactionId)`; covering index for balance queries |
| `transactions` | `(OccurredOn)`, `(SourceKind, SourceId, OccurredOn)` |
| `transactions` | UNIQUE `(SourceKind, SourceId, OccurredOn)` where `SourceKind` in (`Recurring`,`Accrual`) — the idempotency guard. For a recurring transaction, `SourceId` is the `RuleId` and `OccurredOn` is the occurrence date; for an accrual, `SourceId` is the `InvestmentTerms.Id` and `OccurredOn` is the accrual date. This is the physical form of the `(RuleId, OccurrenceDate)` uniqueness described in 5.8 and the accrual uniqueness described in 5.7. |
| `accounts` | UNIQUE `(ParentAccountId, Name)`; index on `Path` |
| `pending_occurrences` | UNIQUE `(RuleId, OccurrenceDate)` |
| `pocket_allocations` | `(PocketId, PeriodKey)` |
| `budgets` | partial unique on `(CategoryAccountId, PeriodType)` where `IsActive` |
| `postings` | CHECK `AmountMinor <> 0` |
| `accounts` | CHECK enforcing the Kind/Role consistency rules from 5.2 |

The zero-sum invariant (I1) cannot be expressed as a SQLite row check, so it
is enforced in the domain aggregate **and** verified by a
`SELECT TransactionId FROM postings GROUP BY TransactionId HAVING SUM(AmountMinor) <> 0`
integrity check that runs on startup and on demand from the admin screen.

### Migrations, backup and export

- EF Core migrations run at startup, **after** an automatic pre-migration
  backup copy.
- The Settings screen's **Back up now** uses SQLite's Online Backup API to
  produce `money-YYYYMMDD-HHmmss.db`. Retention count is a setting; default 10.
- Settings' **Download JSON** emits the entire ledger, accounts, budgets,
  pockets and investment terms in a documented, re-importable shape. **Download
  CSV** emits a flat postings table for spreadsheet use.
- A restore path exists in the admin screen: pick a backup, the app validates
  it, swaps the file and restarts.

---

## 9. Operations

**There is no HTTP API.** One drafted here, `/api/v1` with an OpenAPI document,
was built through phase 3 and removed on 9 September 2026: the desktop window
was its only client and it never called it, so it was thirty routes and 1,300
lines of tests guarding a surface nobody used. Every operation below is a page
handler (`/transactions?handler=QuickAdd`) calling the same application-layer
use case the route used to call. The list stays as the inventory of what the
app has to be able to do; the paths are how it was drafted, not what it serves.

```
GET    /api/v1/accounts                     ?kind=&role=&includeArchived=
POST   /api/v1/accounts
PATCH  /api/v1/accounts/{id}
POST   /api/v1/accounts/{id}/archive
GET    /api/v1/accounts/{id}/balance        ?asOf=

GET    /api/v1/categories                   tree view, sugar over accounts
POST   /api/v1/categories

GET    /api/v1/transactions                 ?from=&to=&accountId=&categoryId=&q=&cursor=&limit=
POST   /api/v1/transactions                 honours Idempotency-Key header
GET    /api/v1/transactions/{id}
PUT    /api/v1/transactions/{id}
POST   /api/v1/transactions/{id}/void
POST   /api/v1/transactions/quick-entry     sugar: amount, category, account, date;
                                            the category's Kind decides spend vs income
POST   /api/v1/transactions/transfer        sugar: from, to, amount, date

GET    /api/v1/recurring-rules
POST   /api/v1/recurring-rules
PATCH  /api/v1/recurring-rules/{id}
POST   /api/v1/recurring-rules/{id}/pause
POST   /api/v1/recurring-rules/{id}/resume
DELETE /api/v1/recurring-rules/{id}
GET    /api/v1/recurring/pending
POST   /api/v1/recurring/pending/{id}/confirm
POST   /api/v1/recurring/pending/{id}/skip
POST   /api/v1/recurring/run                manual catch-up; idempotent

GET    /api/v1/budgets
POST   /api/v1/budgets
PATCH  /api/v1/budgets/{id}
GET    /api/v1/budgets/status               ?period=

GET    /api/v1/pockets
POST   /api/v1/pockets
PATCH  /api/v1/pockets/{id}
POST   /api/v1/pockets/{id}/fund            Real -> transfer; Virtual -> allocation
POST   /api/v1/pockets/{id}/withdraw
GET    /api/v1/pockets/unallocated          ?accountId=

GET    /api/v1/investments
POST   /api/v1/investments
PATCH  /api/v1/investments/{id}
GET    /api/v1/investments/{id}/schedule    ?horizon=   projection, read-only
POST   /api/v1/investments/accrue           idempotent catch-up

GET    /api/v1/dashboard                    ?period=
GET    /api/v1/reports/spend-by-category    ?from=&to=&depth=
GET    /api/v1/reports/cashflow             ?from=&to=&granularity=
GET    /api/v1/reports/net-worth            ?from=&to=&granularity=

GET    /api/v1/settings
PUT    /api/v1/settings
POST   /api/v1/admin/backup
GET    /api/v1/admin/export                 ?format=json|csv
POST   /api/v1/admin/integrity-check
GET    /health
```

The `IdempotencyRecords` table is a leftover of that API: it de-duplicated
retried POSTs, and nothing reads or writes it now. It is dropped whenever a
migration is next written.

---

## 10. User interface

Server-rendered Razor Pages with HTMX for partial updates. Responsive by
construction, so the same pages work in the desktop window and on a phone
browser. A PWA manifest and a minimal service worker ship in v1 so that
server mode later installs to a home screen without further work.

### Screens

| Screen | Contents |
|---|---|
| **Dashboard** | Current period: income, spending, net, delta vs previous period. Spend-by-category donut. Budget bars with state colours. Pocket progress bars. Net worth and investment value. Upcoming recurring items in the next 14 days. Pending confirmations needing action. |
| **Transactions** | One whole month per screen, newest first — no paging control and no infinite scroll, so the table is as tall as the month is busy. Date/account/category/text filters, inline quick-add row, split editor, void action. Swapping a month must not move the reader: see "Swapping data into a page" in CLAUDE.md. |
| **Categories** | Tree editor: create, rename, reparent, archive, colour, icon. |
| **Accounts** | Bank/cash accounts with balances, opening-balance wizard, archive. |
| **Budgets** | One row per budget: limit, spent, remaining, state; period switcher. |
| **Pockets** | Cards for real and virtual pockets with progress, fund/withdraw, unallocated remainder per backing account. |
| **Investments** | Terms editor, posted interest history, projection chart to maturity, accrue-now action. |
| **Recurring** | Rule list with next occurrence; pending-confirmation inbox. |
| **Settings** | Currency, period anchor and time zone, backup, export, restore, integrity check. |

The **quick-add** control is the single most important UX element: date
defaults to today, amount field autofocused, category as a type-ahead,
account remembered from last use, `Enter` saves. Success criterion 1 depends
on it.

### First run

A short wizard: pick base currency and period anchor, create the first cash
or bank account with an opening balance, and seed a starter category tree
(Housing, Groceries, Eating out, Alcohol, Gaming, Transport, Health,
Subscriptions, Other) that the user can rename or delete.

---

## 11. Hosting

One binary, one shape: `MoneyApp.exe` binds Kestrel to `127.0.0.1:<free port>`,
opens a WebView2 window on it, requires no authentication, and enforces a
single instance with a named mutex.

`--headless` skips the window, the runtime check and the mutex, and takes its
address from `--urls`. It exists so CI can start the published executable on a
runner with no browser runtime and wait for `/health`; it is not a server mode
and there is no authentication behind it.

There is no JSON API. The window talks to Razor page handlers, those talk to
the application layer, and nothing else is a client. Serving this UI to another
machine was phase 9 and is not being built - see section 13.

### Desktop packaging

- `dotnet publish -r win-x64 -c Release -p:PublishSingleFile=true --self-contained true -p:PublishTrimmed=false`
- Trimming is **disabled deliberately**: EF Core and Razor rely on reflection,
  and a trimmed build fails at runtime in ways that are painful to diagnose.
  The size cost is acceptable for a personal application.
- WebView2 Runtime presence is checked at startup; if absent, the app shows a
  clear message and a link to the Evergreen bootstrapper rather than crashing.
- Output is a single `.exe` with no installer.

### Server mode (deferred to phase 9)

Multi-stage Dockerfile producing a `linux-x64` image, non-root user, data
volume at `/data`, `HEALTHCHECK` against `/health`. Authentication is a single
password with ASP.NET Core cookie auth and a rate limiter — deliberately
minimal, and adequate for a LAN service. HTTPS on the LAN is documented as a
reverse-proxy concern rather than built in.

---

## 12. Testing strategy

Test-first throughout: a failing test precedes every behaviour change.

### Layers

| Layer | Scope | Speed |
|---|---|---|
| `Domain.Tests` | Value objects, invariants, `PeriodResolver`, `AccrualEngine`, `ProjectionEngine`, `ScheduleExpander`, budget calculation | Milliseconds, no I/O |
| `Application.Tests` | Use cases against real SQLite (`:memory:` with the connection held open) so real constraints and real SQL run | Fast |
| `Api.Tests` | `WebApplicationFactory`, full request/response, Problem Details shapes, idempotency | Moderate |
| `Architecture.Tests` | NetArchTest rules from section 7 | Fast |

An in-memory *provider* is deliberately not used; only real SQLite exercises
the check constraints and unique indexes that carry part of the safety
argument.

### Property tests (CsCheck)

These, not the example tests, are where the confidence comes from:

1. For any generated sequence of transactions, every persisted transaction's
   postings sum to zero. *(I1)*
2. For any generated sequence, an account's balance equals the sum of its
   postings, and equals the balance recomputed from scratch. *(I3)*
3. For any principal, rate, frequency and term, the accrual total matches the
   closed-form compound-interest value to within one minor unit. *(I7)*
4. Expanding a schedule twice over the same window yields an identical
   occurrence set; materialising twice creates no second transaction. *(I8)*
5. For any date and any `PeriodType`, the date falls in exactly one period,
   and consecutive periods share a boundary exactly. *(I9)*
6. For any generated ledger containing transfers, pocket funding and
   investment purchases, total reported spending equals the sum of postings
   to `Kind=Expense` accounts only. *(I12)*

### Additional gates

- **Golden-file test** for the dashboard read model against a fixed seeded
  dataset, so a change in reporting logic is always visible in a diff.
- **Mutation testing** (Stryker.NET) on `Money.Domain`, run in CI with a score
  threshold. Line coverage alone is not accepted as evidence for money logic.
- **Migration test**: apply all migrations from empty, seed, apply again,
  assert idempotence and that the integrity check passes.
- **Determinism**: no test reads the system clock; `FakeClock` everywhere.

### CI

GitHub Actions on push and pull request: restore, build with warnings as
errors, `dotnet test` with coverage, architecture tests, and Stryker on the
domain project.

---

## 13. Delivery phases

Each phase is independently shippable and leaves the application working.

| # | Phase | Deliverable | Acceptance |
|---|---|---|---|
| 0 | Foundation | Solution, five projects, four test projects, CI, `Money`, `Currency`, `PeriodKey`, `PeriodResolver`, `IClock` | Green pipeline; architecture tests pass; period tiling property test passes |
| 1 | Ledger core | Accounts, category tree, transactions, postings, balancing, balance queries | I1–I5, I11, I12 hold under property tests |
| 2 | Persistence + shell | EF Core, migrations, SQLite, backup, export, integrity check, Minimal API, HTMX shell, transactions and categories screens, first-run wizard | **A usable manual expense tracker.** Round-trip: add expense, see it in the list, see the balance change |
| 3 | Recurring | `ScheduleExpander`, `RecurringMaterialiser`, rules screen, pending inbox | Rent posts once and only once when the materialiser runs three times; day-31 rules behave in February |
| 4 | Budgets | Budget entity, status calculation, rollover, budgets screen | Budget status matches hand-computed values; overlap validation rejects nested subtrees |
| 5 | Pockets | Real and virtual pockets, allocations, fund/withdraw, pockets screen | I6 holds; funding a real pocket produces a transfer that appears in no spending report |
| 6 | Investments | Terms, `AccrualEngine`, `ProjectionEngine`, investments screen | I7 holds; running accrual twice posts nothing extra; projections never appear in net worth |
| 7 | Dashboard | Read model, charts, reports endpoints | Golden-file test passes; dashboard renders in under 200 ms on a 10k-transaction dataset |
| 8 | Packaging | Single-file self-contained publish, WebView2 host, single-instance guard, runtime check, window state persistence | Double-clicking `MoneyApp.exe` on a clean Windows 11 machine opens a working app |
| ~~9~~ | ~~Server mode~~ | Dropped 9 September 2026: this is a desktop app, so the flag, the JSON API, the PWA manifest and service worker, and the `HostingMode`/`ICurrentUser` seams that existed only to make it a registration change were all removed. | |

Phases 0–2 are the critical path to something the user can actually use
daily. Phases 3–7 add the requested features on top of a proven core. Phase 8
turns it into the executable that was asked for.

---

## 14. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Database file on a OneDrive-synced folder corrupts under WAL | Total data loss | Data lives in `%APPDATA%`; automatic backups; documented in first-run wizard |
| Single-file publish breaks EF Core or Razor via trimming | App fails only in the packaged build | `PublishTrimmed=false`; a smoke test runs the **published** executable in CI, not just `dotnet run` |
| WebView2 Runtime absent | App fails to open on a fresh machine | Startup check with a clear message and bootstrapper link |
| Rounding drift in long investment terms | Balances silently diverge from the bank's | Carried-remainder accrual; property test against the closed form |
| Recurring rules double-post | Fabricated expenses; corrupted history | Unique index at the database level, not only in code; explicit repeat-run tests |
| DST and time-zone edges shift period boundaries | Transactions land in the wrong month | All period arithmetic in one class, in a configured IANA zone, with DST-crossing test cases |
| Double-entry vocabulary leaks into the UI | Unusable for the intended user | The word "posting" never appears in a view; a UI review at the end of phases 2 and 7 |
| Sign convention confusion in reports | Income shows negative, expenses inverted | Convention documented in section 5.3, applied in one mapper, asserted in API tests |
| Over-engineering the layering for a solo app | Slow progress | Five projects is the floor for the dependency rule to be enforceable; `Money.Web` was deliberately merged into `Money.Api` |
| Scope creep toward market-priced investments | Phase 8 never ships | Section 2 is the contract. Bank import was later approved explicitly and is no longer deferred; market-priced holdings still are |

---

## 15. Assumptions

1. Base currency is single and set at first run; the schema stores a currency
   code on every amount so multi-currency is a later feature, not a migration
   crisis.
2. The user runs Windows 11 with WebView2 present (the default) and will
   install the .NET 9 SDK to build.
3. Historical data is entered manually or starts from an opening balance. The
   bank feed only reaches back as far as the ASPSP exposes (about 90 days),
   so it is a keep-up mechanism, not a backfill.
4. Data volume stays in the tens of thousands of transactions, so aggregate
   queries run directly against `postings` with no materialised summary
   tables. A `period_summary` table is added only if measurement shows it is
   needed — not before.
5. "Automatic" means recurring rules plus interest accrual. No bank
   connection exists, so nothing else can be automatic; fast manual entry is
   the compensating design goal.

---

## 16. Next step

Turn this specification into a step-by-step implementation plan
(`superpowers:writing-plans`), phase by phase, with a failing test named for
every behaviour before its implementation.
