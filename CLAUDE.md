# Money Tracker — working agreement

Local-first personal money tracker. .NET 9, EF Core + SQLite, Razor + HTMX,
shipped as a single-file WebView2-hosted `.exe`.

**Desktop only.** There is no JSON API, no PWA manifest, no service worker and
no server mode. The window is the only client, and it talks to Razor page
handlers. Do not add an HTTP endpoint for something a page handler can do.

Full design: [docs/superpowers/specs/2026-09-01-money-tracker-design.md](docs/superpowers/specs/2026-09-01-money-tracker-design.md).
That spec is the contract — this file is the subset you must not violate while
coding. When the two disagree, the spec wins and this file gets fixed.

**Status:** implemented and running. Phases 0–2 shipped the ledger core, SQLite
persistence and the HTMX shell — a usable manual expense tracker. On top of
that came recurring entries (phase 3), multi-currency accounts and a home page
you record from and step through a month at a time. Phase 8 (single-file
publish, the WebView2 desktop shell, single-instance guard, window state and
restore-from-backup) is the most recent. Then, on 9 September
2026, everything that existed only to serve this UI to a browser was removed:
the `/api/v1` surface and its OpenAPI document, `HostingMode`, `ICurrentUser`,
the idempotency filter, the PWA manifest and service worker, and Money.Api's
launch profiles. Bank sync went the same day — the Enable Banking feed, its
client and options, `ImportBankTransactionsHandler`, `LedgerTemplates.Imported`
and the Home screen's sync form are all gone. Still unbuilt: budgets (4),
pockets (5), investments and accrual (6) and the reports dashboard (7). Phase
9, server mode, is dropped. CSV import is still deferred, not dropped.
The solution, projects and test commands below exist; match the shape there.

Removing the API took three capabilities with it, because they had a route and
no screen: transfer between accounts, an entry split across several categories,
and a balance as of a past date. Transfer has a screen again - "Move money
between accounts" in the Record something sheet, which posts to the same
`QuickAdd` handler. The other two handlers are still in `Money.Application` and
still tested, and each needs a screen, not an endpoint.
See section 8 of [docs/using-the-app.md](docs/using-the-app.md).

---

## Sign convention

Fixed, documented once, tested hard:

> **Positive = debit. Negative = credit.**
> Asset and Expense accounts increase with a **positive** amount.
> Income, Liability and Equity accounts increase with a **negative** amount.

Income accounts therefore carry a negative internal balance. The presentation
layer negates them for display, and that negation lives in **exactly one
mapper**. Do not negate anywhere else, do not "fix" a negative income balance
in a query, a DTO or a Razor view.

Worked examples (minor units):

| Event | Postings |
|---|---|
| Salary 3000 into current account | Bank `+300000`, Income:Salary `-300000` |
| Dinner 20 from current account | Expense:Food `+2000`, Bank `-2000` |
| Transfer 500 to savings pocket | Asset:SavingsPocket `+50000`, Bank `-50000` |
| Interest 12.34 capitalised | Asset:Investment `+1234`, Income:Interest `-1234` |
| Opening balance 1000 | Bank `+100000`, Equity:OpeningBalance `-100000` |

Money is `long` minor units + ISO-4217 code. Rates are `decimal`. `double` and
`float` never appear in `Money.Domain`; SQLite `REAL` is never used.

---

## Invariants

These are the tested contract. Any change that breaks one is wrong, no matter
how convenient.

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

I1, I3, I7, I8, I9 and I12 are **property tests** (CsCheck), not example tests.

Consequences worth stating plainly:

- Balances are always `SUM(AmountMinor)` over non-voided postings. Never a
  stored field, never a cached column.
- Transactions are **voided, never deleted** — no exception, including when the
  account or category they name is deleted.
- Reference data is **archived by default**. Deleting is a separate, explicitly
  requested and irreversible action, and archiving first is required. A subtree
  no posting refers to is erased outright; a subtree the ledger still names
  keeps its rows with `IsDeleted`, out of every list, picker, archive and
  name-collision check, so old entries can still say what they were for.
  `AccountDisplayName` is the only place that marks such a name for a reader.
- Categories are accounts (`Kind=Income|Expense, Role=Category`) in the same
  tree. There is no `CategoryId` column, and spending reports are a query
  restricted to `Kind=Expense` — that is what makes I12 structural.
- Recurring materialisation and interest accrual are idempotent, guarded by a
  unique index in the database, not only in code:
  `UX_Transactions_Source_Idempotency` over `(SourceKind, SourceId,
  OccurredOn)`. `ExternalRef`,
  `SourceKind=Import` and `UX_Transactions_Import_ExternalRef` are left in the
  schema by the removal of bank sync — nothing writes them now, and a CSV
  import would want them back.
- The `ProjectionEngine` is read-only. Projected interest is never written as
  a transaction and never enters net worth.
- `PeriodResolver` is the only component that computes period boundaries.
  Nothing else does month arithmetic.
- `IClock` is injected everywhere; there is no ambient time. `DateTime.Now` /
  `UtcNow` outside the composition root fails the build.

---

## Layering rule

```
src/
  Money.Domain/          entities, value objects, invariants, pure engines
  Money.Application/     use cases, ports, validation, DTOs
  Money.Infrastructure/  EF Core, migrations, repositories, clock, backup
  Money.Api/             Razor Pages + HTMX, page handlers, composition root
  Money.Desktop/         WebView2 host, boots Kestrel on loopback
tests/
  Money.TestSupport/         FakeClock, SqliteFixture, ledger builders (not a test project)
  Money.Domain.Tests/        fast unit + property tests, no I/O
  Money.Application.Tests/   use cases against real in-memory SQLite
  Money.Api.Tests/           WebApplicationFactory integration tests
  Money.Architecture.Tests/  NetArchTest rules
```

Dependencies point **inward only**:
`Desktop -> Api -> Infrastructure -> Application -> Domain`.

`Money.Architecture.Tests` fails the build if:

- `Money.Domain` references any other project or any third-party package
- `Money.Application` references `Money.Infrastructure` or `Money.Api`
- `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` or
  `DateOnly.FromDateTime(DateTime.Now)` appear outside the composition root
- `double` or `float` appear anywhere in `Money.Domain`
- any public domain type exposes a mutable collection

Also non-negotiable at the boundaries:

- Double-entry vocabulary never reaches a view. The word "posting" does not
  appear in the UI.
- Expected failures return `Result<T>`; exceptions are for programmer error
  only. A page handler puts `result.Error.Message` on the screen; it never
  throws to say no. `AddProblemDetails` stays registered for what the framework
  raises on its own (a failed antiforgery check is a 400 because of it).
- `--headless` is Money.Desktop's flag and lives only there: no window, no
  WebView2 check, no single-instance mutex, address from `--urls`. It exists
  for CI's smoke test against `/health` and is not a hosting mode. Nothing
  below `Money.Desktop` knows it was passed.

---

## Swapping data into a page

Every list on screen is swapped in by HTMX, and any swap that changes how much
content the page holds can move the reader. Learned the hard way on the month
stepper, and the same trap is waiting for every future list that grows and
shrinks with its data:

1. **A list is never given a height.** No `min-height`, no fixed height, no
   space reserved "so the page does not jump". A table is exactly as tall as
   its rows are. Reserving height trades a jump for a blank gap that outlives
   the swap - worse, because the reader can see it and cannot get rid of it.
   Releasing that reserve later from a scroll handler does not save it: on a
   page the reader never scrolls, the gap simply stays.
2. **Restore the scroll position instead.** A swap that replaces
   `<main id="content">` (`hx-target="#content" hx-swap="outerHTML"`, which is
   what `hx-boost` on the month arrows and the filter form does) leaves the
   document briefly near-empty, so the browser clamps scroll to the top of what
   is left. That clamp *is* the jump - nothing in htmx caused it. Read
   `window.scrollY` on `htmx:beforeSwap` and put it back on **both**
   `htmx:afterSwap` and `htmx:afterSettle`: htmx leaves ~20ms between the two,
   long enough for the clamp to be seen.

The handler lives once, at the foot of `_Layout.cshtml`, and keys on the swap
target's id being `content`. Any new swap of that target inherits it - do not
add a second copy, and do not reach for a per-page variant.

`show:none` in `hx-swap` suppresses htmx's own scroll-to-top on a boosted link.
It does **not** suppress the browser's clamp. Both are needed; neither replaces
the other.

The one case where both rules cannot hold: at the very bottom of a long month,
stepping to a much shorter one. That scroll position does not exist in the
shorter document, so the reader lands at its new bottom. Accepted - the only
way out is the reserved blank space rule 1 forbids.

Verify a change here in the running window, not by reasoning: measure
`window.scrollY` and `document.documentElement.scrollHeight` across the swap.
See [docs/running-locally.md](docs/running-locally.md) for a scratch instance
to do it in.

---

## Test commands

```bash
dotnet test
```

```bash
dotnet test tests/Money.Domain.Tests
```

```bash
dotnet test tests/Money.Architecture.Tests
```

```bash
dotnet build -warnaserror
```

Mutation testing gates `Money.Domain` — line coverage is not accepted as
evidence for money logic:

```bash
dotnet stryker --project Money.Domain.csproj
```

CI (GitHub Actions, on push and PR) runs: restore, build with warnings as
errors, `dotnet test` with coverage, architecture tests, and Stryker on the
domain project.

Testing rules:

- **Test-first.** A failing test named for the behaviour precedes every
  behaviour change.
- `Application.Tests` use **real SQLite** (`:memory:` with the connection held
  open), never the EF in-memory provider — the check constraints and unique
  indexes carry part of the safety argument and must actually run.
- No test reads the system clock. `FakeClock` everywhere.
- The dashboard read model has a golden-file test against a fixed seeded
  dataset; reporting changes must show up as a diff.

---

## Branching

Work directly on `main`. Do not create feature branches or worktrees for this
project unless explicitly asked.
