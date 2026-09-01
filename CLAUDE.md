# Money Tracker — working agreement

Local-first personal money tracker. .NET 9, Minimal API, EF Core + SQLite,
Razor + HTMX, shipped as a single-file WebView2-hosted `.exe`.

Full design: [docs/superpowers/specs/2026-09-01-money-tracker-design.md](docs/superpowers/specs/2026-09-01-money-tracker-design.md).
That spec is the contract — this file is the subset you must not violate while
coding. When the two disagree, the spec wins and this file gets fixed.

**Status:** spec approved, implementation not started. The solution, projects
and test commands below do not exist yet; create them as described, do not
invent a different shape.

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
- Transactions are **voided, never deleted**. Reference data is **archived,
  never deleted**.
- Categories are accounts (`Kind=Income|Expense, Role=Category`) in the same
  tree. There is no `CategoryId` column, and spending reports are a query
  restricted to `Kind=Expense` — that is what makes I12 structural.
- Recurring materialisation and interest accrual are idempotent, guarded by a
  unique index in the database, not only in code.
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
  Money.Api/             Minimal API, Razor Pages + HTMX, composition root
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
  only. The application layer maps domain errors to RFC 9457 Problem Details.
- Hosting mode (desktop vs server) is a config flag read at startup. Nothing
  below the API layer knows which mode it is in.

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
