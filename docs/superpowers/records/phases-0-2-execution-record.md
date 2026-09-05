# Money Tracker — execution record, phases 0–2

Controller ledger from the subagent-driven execution of
[the phases 0-2 plan](../plans/2026-09-01-money-tracker-phases-0-2.md), preserved because it holds 48
recorded rulings and the reasoning behind them. Phases 3–8 get their own plans written against this
code; this file is what tells their author which decisions were deliberate and what each cost if wrong.

Verified gates at completion: `dotnet build -warnaserror` 0 warnings/0 errors; `dotnet test` 413 passing;
`dotnet stryker` 93.20% against a threshold of 80.

---

# SDD ledger — plan: docs/superpowers/plans/2026-09-01-money-tracker-phases-0-2.md

Spec: docs/superpowers/specs/2026-09-01-money-tracker-design.md (read in full)
Branch: feat/money-tracker-phases-0-2 (branched from main @ 6d4e4ea)
Pre-task commit: 847d0ef docs: record tests/Money.TestSupport in the project layout

## Pre-flight scan

Method: per-task file-path extraction across all 33 tasks (producer/consumer
table below), plus full reads of Tasks 1 and 2 (they constrain every later
task) and targeted greps for the cross-cutting hazards (namespace collision,
Stryker threshold, sign convention, ambient time). Tasks 3-33 were NOT read in
full by the controller; their internal self-consistency is delegated to the
per-task review loop, which reads each brief against each diff. Recorded so the
limit of this scan is visible.

### Shared-file / interface pairs

| Producer | Consumer | Handoff | Finding |
|---|---|---|---|
| T1 project graph | T2 arch tests | Architecture.Tests -> Api, Desktop; Domain reached transitively | OK — transitive ProjectReference makes `typeof(Money.Domain.DomainAssemblyMarker)` compile |
| T2 ApplicationAssemblyMarker.cs | T17 Application/ | T17 adds files beside the marker | OK — no edit of the marker |
| T1 Money.Domain.csproj | T7 | T7 touches the csproj (period vocabulary) | OK — additive |
| T1 Money.Domain.Tests.csproj | T9 | T9 adds Stryker tooling | OK — additive |
| T6 IClock (Domain) | T6 FakeClock (TestSupport), SystemClock (Infrastructure) | one interface, two impls | OK — SystemClock path is on T2's ambient-time allow-list verbatim |
| T13 Transaction.cs | T14 Transaction.cs | T14 extends the T13 aggregate (void/replace) | OK — same file, strictly later |
| T15 LedgerGen (TestSupport) | T16 I12 property test | generated ledgers with transfers/pockets/investments | OK |
| T15 BalanceCalculator | T20 LedgerQueries | SQL must agree with the domain calculator | OK — T20 names BalanceCalculator as the specification |
| T18 SqliteFixture (TestSupport) | T20-T25 Application.Tests | real SQLite :memory:, connection held open | OK |
| T17 DisplayAmountMapper | T21-T24 mappers, T27-T32 pages | the only sign negation | OK — no other task's file list contains a negation site |
| T30 TransactionsPageTests.cs | T32 same file | T32 amends a T30 test file | OK — strictly later, flagged for the T32 reviewer |
| T26 Program.cs | T27-T32 endpoints/pages | composition root registers everything later | OK |

### Self-consistency findings and rulings

- **Duplicate global using.** T1 Step 4 tells each test project to create
  `GlobalUsings.cs` with `global using Xunit;`, but the `dotnet new xunit`
  template already emits `Usings.cs` containing that line. Two identical global
  usings in one project is CS0105, which `TreatWarningsAsErrors=true` (T1 Step 3)
  turns into a build failure — T1 Step 7 would fail as written.
  **Ruling:** delete the template's `Usings.cs` and keep one `GlobalUsings.cs`
  per test project carrying both lines. Cost if wrong: none — the two files are
  interchangeable; only the duplication is fatal.
- **"nine projects".** T1 Step 9's commit message says nine; the plan's own File
  Structure lists ten (5 src + 5 tests).
  **Ruling:** commit message says ten. Cost if wrong: one word in a commit message.
- **Namespace/type collision** `Money.Domain.Money.Money`. Already anticipated by
  the plan (line 932): test files alias `using MoneyValue = Money.Domain.Money.Money;`.
  No ruling needed — recorded so a reviewer does not flag it as a defect.
- **Stryker threshold 80** (T9 `break: 80`, T16 and T33 re-runs) matches the
  operator's instruction. No conflict.
- **Ambient-time arch test scans `src` only**, not `tests`. The Global Constraint
  "no test reads the system clock" is therefore convention + review, not enforced.
  **Ruling:** leave as the plan writes it; FakeClock is mandated per-test and the
  reviewer checks it. Cost if wrong: a future test could read the clock unnoticed.

### Controller rulings (process)

- **Workspace.** Work happens on branch `feat/money-tracker-phases-0-2` in the
  primary working directory, not a separate git worktree. The operator pointed at
  this directory, there is no concurrent work, and a worktree would move every
  subagent's cwd off the path the dispatch names. Cost if wrong: the branch is
  not isolated from the main checkout; recoverable by `git switch`.
- **tests/Money.TestSupport kept**, per the operator's explicit instruction, and
  CLAUDE.md's project layout updated in 847d0ef.

## Progress

Task 1: complete (commits 847d0ef..77c181a, review clean — spec compliant, quality approved)
  Verified by controller: `dotnet build -warnaserror` 0 Warning(s) 0 Error(s); `dotnet test` 4 assemblies, 4/4 passed.
  Implementer additions accepted by review as necessary: tests/.editorconfig (CA1707 off for tests/ only,
  because AnalysisLevel=latest-recommended makes the plan's own underscored test names hard errors), and
  SolutionSmokeTests.cs placeholders in the three test projects the brief left empty (an empty test project
  reports "No test is available", not "Passed!", so the brief's Step 7 gate was otherwise unsatisfiable).
Task 1: minor (deferred): Money.sln nests the five test projects under a "src" solution folder — cosmetic,
  inherited from the brief's own `find src tests ... | dotnet sln add` command. VS Solution Explorer only.
Task 1: minor (deferred): generated csproj files redundantly repeat TargetFramework/Nullable/ImplicitUsings
  that Directory.Build.props already sets. Values agree; template default.
Task 2: BLOCKED — environment, not code. Implementer wrote all five architecture tests + RepositoryRoot +
  both assembly markers (untracked in the working tree, NOT committed). Build is clean; Step 7 RED evidence
  captured (AmbientTimeTests fails naming ApplicationAssemblyMarker.cs, passes after removal).
  `dotnet test tests/Money.Architecture.Tests` -> 5 of 7 failed with
  `System.IO.FileLoadException : Could not load file or assembly '...\Money.Domain.dll'.
   An Application Control policy has blocked this file. (0x800711C7)`
  Controller-verified diagnosis (the implementer's "path-based" theory was WRONG):
    - HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState = 1
      => Windows Smart App Control is ON in enforcement mode.
    - Desktop is NOT OneDrive-redirected ([Environment]::GetFolderPath('Desktop') = C:\Users\janos\Desktop).
    - The blocked DLL carries no Mark-of-the-Web stream (only :$DATA).
    - Copying the whole tree to %TEMP% and running there is WORSE, not better: SAC then blocked
      Money.Architecture.Tests.dll itself ("could not load dependent assembly"). So relocating the repo
      does not fix it, and no MSBuild output-path redirect can.
    - Task 1's `dotnet test` passed because its smoke tests never load Money.Domain.dll. Which freshly
      built unsigned assembly SAC blocks varies per build (every rebuild is a new hash SAC re-evaluates),
      so this is an intermittent, worsening failure across the remaining 31 tasks, not a one-off.
  SAC has no exclusion list, and disabling it is a security-setting change I am prohibited from making
  (and it is irreversible without reinstalling Windows). Stopping to ask the operator. WSL2 is present
  but has only the `docker-desktop` distro; Docker Desktop is installed.
Task 2: blocker RESOLVED — operator disabled Smart App Control (VerifiedAndReputablePolicyState now 0).
  Controller re-ran `dotnet test tests/Money.Architecture.Tests` -> Passed! Failed: 0, Passed: 7, Total: 7.
  Implementer resumed to re-take Step 7 RED evidence cleanly, correct the (wrong) path-based claim in its
  report, and commit. Ruling: no code change was needed; the plan was never at fault.
Task 2: review 1 — spec structure OK, Step 7 RED evidence confirmed, violation absent from the commit.
  4 Important findings, ALL plan-mandated (the brief supplied the exact reflection code verbatim):
    F1 DomainPurityTests.cs:59 `t.IsPublic` is false for ANY nested type -> a public nested domain type
       exposing a mutable collection is skipped silently.
    F2 DomainPurityTests.cs:60 scans only public INSTANCE PROPERTIES -> public fields, static properties
       and methods returning List<T> are invisible to the "exposes a mutable collection" rule.
    F3 DomainPurityTests.cs:29 `GetMethods` never returns constructors -> `public Money(double amount)`
       passes the floating-point rule undetected.
    F4 DomainPurityTests.cs:45 `IsFloating` unwraps only Nullable/array/by-ref -> `IReadOnlyList<double>`
       evades BOTH the float rule and the mutable-collection rule.
  Ruling: fix all four. The spec (section 7) requires the rules to fail the build when "double or float
    appear ANYWHERE in Money.Domain" and when "ANY public domain type exposes a mutable collection". The
    plan's code implements something narrower than the spec demands, so the spec wins over the plan text.
    These are strengthenings, never weakenings — consistent with the operator's standing rule that a test
    is never weakened to pass. Each strengthened rule must be shown failing on a planted violation before
    it is trusted, the same way brief Step 7 proves the ambient-time rule.
    Cost if wrong: a stricter architecture test could false-positive on a legitimate domain shape in a
    later task; that surfaces as a loud build failure, not a silent hole, and is cheap to narrow then.
Task 2: minor (deferred): DependencyRuleTests.cs:18 "starts with System" would admit a third-party package
  named System.* (e.g. System.Reactive) despite the assertion message claiming zero third-party deps.
Task 2: minor (deferred): AmbientTimeTests Forbidden regex is defeated by `using static System.DateTime;`
  (bare `UtcNow`). Unusual idiom; noted, not fixed.
Task 2: fix round 1/5 (4 addressed, 0 open — F1 IsVisible, F2 fields/statics/method returns, F3 constructors,
  F4 generic-argument recursion; commits bb4c1f2..d3bcfa0). Each proven RED on a planted violation the old
  code missed (NestedOffender.Tags, GetTags (return), ..ctor(amount), .Rates), all reverted before commit.
  Re-review traced the widened rules against record-generated members, enums, decimal and IReadOnlyCollection
  and found no false positives. Deferred minors untouched.
Task 2: complete (commits e9ae0e5..d3bcfa0, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; dotnet test 5 assemblies green, Architecture.Tests 7/7.
Task 2: minor (deferred): IsFloating unwraps only one array level, so double[][] evades. Not a realistic
  domain shape.
Task 2: minor (deferred): IsSpecialName skip means a conversion operator returning a mutable collection
  evades the rule. Indexers unaffected.
Task 3: review 1 — spec compliant (files byte-identical to the brief; RED/GREEN TDD evidence real).
  1 Important finding, plan-mandated:
    F1 Result.cs:222-247 — default(Result<T>) has IsSuccess=false and Error=null, so Match invokes
       onFailure(null) and Map produces another null-Error failure. Any handler doing e.Code after
       confirming IsFailure gets an NRE far from the actual bug. Reachable via an uninitialised field,
       a `new Result<T>[n]`, or a test double returning default. Value already guards; Error/Map/Match
       do not.
  Ruling: fix it, and bundle the Minor test-rigor finding below into the same round. The guard adds no
    signature change, so Tasks 4-33 are unaffected; DomainError is non-nullable in Fail(), so
    `IsFailure && Error is null` is reachable ONLY from the default struct and the guard can never fire
    on correct code. Converting a silent downstream NRE into a loud diagnostic throw is exactly the
    spec's "exceptions are for programmer error only" rule, the same way Value already behaves.
    Cost if wrong: a few unreachable lines.
  Ruling: bundle the Minor "Map test does not prove laziness" finding into this round rather than
    deferring it. Process says minors do not enter the loop, but the loop is already open for F1 and
    the marginal cost is one test case on a money-path primitive. Cost if wrong: negligible.
Task 3: fix round 1/5 (2 addressed, 0 open — F1 ErrorOrThrowIfDefault guards Map/Match; F2 throwing-projection
  test proves laziness; commits c1cc942..2e48908). Re-reviewer's judgment on the non-generic Result accepted:
  it has no non-nullable handoff to guard, and `r.Error.Code` on it is a CS8602 COMPILE error under
  warnaserror, so the type system already does the job. F2 had no true RED (Map was never broken) — noted,
  not treated as a test-first violation.
Task 3: complete (commits 09b0ddb..2e48908, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 10/10, Architecture.Tests 7/7, others 1/1.
Task 3: minor (deferred): the CA1000 build failure is described narratively in the report without pasted
  compiler output. Auditability nit only; the RED/GREEN evidence itself is fully pasted.
Task 4: review 1 — spec compliant. Reviewer independently verified the ISO-4217 exponents (JPY=0, rest=2),
  static init order (Eur/Huf/Usd declared before KnownByCode, so no null in the table), and that
  Dictionary.ValueCollection cannot be cast back into a mutating handle. 1 Important, plan-mandated:
    F1 Currency.cs:52-61 — Create("EUR", 3) succeeds, yielding a Currency whose Code is EUR but whose
       exponent disagrees with the canonical table entry. Nothing checks KnownByCode. That is exactly the
       silent mis-scale FromCode was designed to prevent, arriving through the other door.
  Ruling: fix it. Task 4's own stated design point is that a currency code must never be paired with the
    wrong exponent ("guessing would silently mis-scale a real amount"), and spec 5.1 defines Currency as
    code + exponent. Create's legitimate purpose is currencies OUTSIDE the table; for a code inside it,
    disagreeing is a defect, not a feature. Task 5's Money scales by MinorUnitExponent, so this becomes
    load-bearing next task. Cost if wrong: Create gets stricter and a hypothetical future caller wanting a
    non-canonical exponent for a known code fails loudly — which is the desired behaviour anyway.
  Ruling: bundle the two Minor test-gap findings (no negative-exponent case, no FromCode(null) case) into
    the same round — two test cases while the file is already open. Cost if wrong: negligible.
Task 4: fix round 1/5 (3 addressed, 0 open — F1 Create now consults KnownByCode after normalisation and
  returns the canonical instance on a match, fails on a disagreeing exponent via a new OVERLOAD of the
  existing DomainErrors.Currency.InvalidMinorUnitExponent factory (no inline code); F2 negative-exponent
  test; F3 FromCode(null) test; commits aaa9011..5ccba48). Unknown codes still creatable with any valid
  exponent, which is what Create is for.
Task 4: complete (commits e40f01e..5ccba48, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 25/25, Architecture.Tests 7/7, others 1/1.
Task 4: minor (deferred): Create_returns_the_canonical_instance_... asserts structural equality, not
  ReferenceEquals, so it would also pass on a fresh copy. Implementation verified correct by inspection
  (`return known`), but the test is weaker than the requirement it covers.
Task 5: review 1 — spec compliant. Reviewer verified by direct code reading: explicit checked(...) at every
  arithmetic site (not a project flag), RoundToMinor is the ONLY rounding call site in Money.Domain and
  ApplyRate routes through it, the -2.5 -> -3 case Task 9's mutation gate depends on is present, and
  ToDecimal scales correctly for exponent 0 and 2. 1 Important, plan-mandated:
    F1 MoneyTests.cs:181-186 — only Add's overflow is asserted. Negate of long.MinValue, Subtract past
       long.MinValue and the unary - path are all unguarded by any test, so a future edit dropping
       `checked` from Negate or Subtract would go undetected. Code is correct today; the regression net
       has holes.
  Ruling: fix it. Task 9 gates Money.Domain at mutation score >= 80, and an unasserted `checked` is exactly
    the shape Stryker mutates into a surviving mutant, so this is load-bearing for a task nine steps away
    as well as for money safety now. Cost if wrong: three extra test cases.
Task 5: minor (deferred): Money.cs:54 the explicit `checked` around the decimal->long cast is redundant
  (decimal conversion range-checks regardless of context). Harmless, self-documenting.
Task 5: minor (deferred): task-5-report.md test-count arithmetic is muddled; the total is right.
Task 5: fix round 1/5 (1 addressed, 0 open — three overflow cases added covering unary operator -, Negate()
  and binary -, each proven to have teeth by removing `checked` and watching them fail, then restoring it;
  commits 17bbb27..a616f08). Fix diff touched only the test file.
Task 5: complete (commits 2c3fa42..a616f08, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 43/43, Architecture.Tests 7/7, others 1/1.
Task 6: review 1 — spec compliant, no Critical/Important. Reviewer confirmed FakeClock reads the real clock
  on NO path (no default ctor, no field initialiser), both At factories build DateTimeOffset with an
  explicit TimeSpan.Zero offset so they are machine-timezone-safe, IClock stayed a single member, and the
  ambient-time allow-list was not touched.
Task 6: complete (commits c57fb4a..db681e7, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 45/45, Architecture.Tests 7/7, others 1/1.
Task 6: minor (deferred): the 5-arg FakeClock.At(y,m,d,hour,minute) overload has no test — a swapped
  hour/minute slot would go undetected. CARRIED INTO TASK 8's dispatch, since PeriodResolver.TodayIn is
  where time-of-day actually matters.
Task 6: minor (deferred): no negative-Advance test. Correct by inspection (UtcNow.Add handles it).
Task 6: note — the reviewer flagged that a single-commit diff cannot itself prove the RED run predated the
  implementation; only the report's pasted output attests it. Inherent to every task in this plan; the
  pasted RED output is the evidence and it is being required each time.
Task 7: review 1 — spec compliant, no Critical/Important. Reviewer checked the two silent-corruption shapes
  explicitly: PeriodType enum values match the brief digit for digit (Weekly=1..Yearly=4, persisted as ints),
  and NO boundary-computing member leaked in — every public member was read and none resolves a date to a
  key, computes a boundary or steps a period. Yearly round-trip pins Index to 1 and survives Parse.
  InternalsVisibleTo added to Money.Domain.csproj for the internal members; no third-party dependency.
Task 7: complete (commits 0908b66..599694f, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 74/74, Architecture.Tests 7/7, others 1/1.
Task 7: minor (deferred): PeriodKey.cs:23 reuses DomainErrors.Period.IndexOutOfRange for a bad YEAR, so
  year 10000 reads "10000 is not a valid index for a Monthly period". Sanctioned by the brief; wants a
  dedicated year-range code eventually.
Task 7: minor (deferred): the year-range branch (year is < 1 or > 9999) has no test.
Task 7: minor (deferred): DateRange rejects end == start correctly but only end < start is tested.
Task 7: minor (deferred): PeriodKey.Parse(null) is safe but untested.
Task 7: minor (deferred): TryFindTimeZone takes string? where the brief wrote string. Harmless.
Task 7: minor (deferred): report's per-file test arithmetic sums to 27, not 29; the total is right.
Task 7: review 1 — spec compliant, no Critical/Important. Reviewer checked the two silent-corruption shapes
  explicitly: PeriodType enum values match the brief digit for digit (Weekly=1..Yearly=4, persisted as ints),
  and NO boundary-computing member leaked in — every public member was read and none resolves a date to a
  key, computes a boundary or steps a period. Yearly round-trip pins Index to 1 and survives Parse.
  InternalsVisibleTo added to Money.Domain.csproj for the internal members; no third-party dependency.
Task 7: complete (commits 0908b66..599694f, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 74/74, Architecture.Tests 7/7, others 1/1.
Task 7: minor (deferred): PeriodKey.cs:23 reuses DomainErrors.Period.IndexOutOfRange for a bad YEAR, so
  year 10000 reads "10000 is not a valid index for a Monthly period". Sanctioned by the brief; wants a
  dedicated year-range code eventually.
Task 7: minor (deferred): the year-range branch (year is < 1 or > 9999) has no test.
Task 7: minor (deferred): DateRange rejects end == start correctly but only end < start is tested.
Task 7: minor (deferred): PeriodKey.Parse(null) is safe but untested.
Task 7: minor (deferred): TryFindTimeZone takes string? where the brief wrote string. Harmless.
Task 7: minor (deferred): report's per-file test arithmetic sums to 27, not 29; the total is right.
Task 8: review 1 — spec compliant, no Critical/Important. Reviewer did independent hand-calculation rather
  than trusting the green suite: proved the midweek rule generalises to ANY FirstDayOfWeek (WeekStart(Jan4)+3
  always lands Jan 1-7, which is exactly what makes ((dayOfYear-1)/7)+1 correct), traced anchor=25 through
  Quarterly (Oct 25 - Jan 25) and Yearly (Jan 25) to confirm the anchor is not silently dropped, and
  confirmed Resolve/Range/Next/Previous never reference _timeZone at all — so DST cannot shift a boundary
  by construction, not merely by test luck. TodayIn converts through the definition's IANA zone.
  Controller independently confirmed all four properties carry iter: 10_000.
Task 8: complete (commits 1afd1be..2185b80, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 90/90, Architecture.Tests 7/7, others 1/1.
Task 8: minor (deferred): AnyFirstDayOfWeek generates only Monday/Sunday/Saturday — 4 of 7 weekday starts
  are structurally unreachable by the property suite. CARRIED INTO TASK 9: if PeriodResolver mutants survive
  Stryker, widening this generator to all seven days is the sanctioned first move (better assertions, never
  a lower threshold).
Task 8: minor (deferred): the property suite fixes the zone to Europe/Budapest. Harmless for I9 (no period
  member touches the zone), but the properties prove nothing about other zones.
Task 8: minor (deferred): "exactly one period" is established algebraically from containment + half-open
  adjacency rather than asserted directly (no property says date is NOT in Range(Previous(key))).
Task 8: minor (deferred): no test exercises TimeZoneInfo.ConvertTime at an ambiguous/nonexistent local
  instant; only TodayIn is zone-sensitive and it has a single example test.
Task 8: note — FakeClock's 5-arg At overload (Task 6 deferred minor) is now incidentally covered by the
  DST test (23:30 UTC -> 01:30 Budapest -> next local date), so that gap is closed.
Task 9: review 1 — spec compliant, no Critical/Important. Thresholds exactly high=90/low=80/break=80,
  .config/dotnet-tools.json is the mechanism (central package management untouched), CI runs
  `dotnet stryker --break-at 80` so it FAILS rather than reports, no Stryker-disable pragma or mutate
  exclusion anywhere, StrykerOutput/ already gitignored. Reviewer read every test hunk: additions only,
  no deletion, no loosened assertion, no Skip=, no reduced CsCheck iteration count. It independently
  re-derived both equivalent-mutant claims and agreed with each.
  CONTROLLER-VERIFIED SCORE: ran `dotnet stryker` from src/Money.Domain myself ->
  "[21:21:24 INF] The final mutation score is 92.05 %". Baseline before this task's test work was 83.44%.
  Suite grew 90 -> 108 tests, all additive.
  Ruling: ACCEPT the Step 5 substitution. The brief's verification scenario (delete the -2.5 rounding case,
    expect a MidpointRounding mutant to survive) is FACTUALLY IMPOSSIBLE — Stryker.NET has no mutator for
    enum-member swaps at any level, so that mutant is never generated. The implementer proved this by
    diffing full before/after mutant status (zero differences), then demonstrated the gate against a mutant
    that does exist: removing the PeriodKey year=1 boundary case drops PeriodKey.cs 100% -> 96.30% and the
    domain 92.05% -> 91.39%. That proves the same causal mechanism the brief wanted proven. This is a plan
    defect, recorded here so the phase 3-8 plans do not repeat it. Cost if wrong: the gate was demonstrated
    on a different mutant than the plan named; the demonstration itself is real either way.
  Reviewer's single Minor (is the equivalence reasoning in the commit body, or only the report?) — checked
  myself: commit 8717394's body carries both justifications AND the Step 5 substitution rationale in full.
  Resolved, not deferred.
Task 9: complete (commits 4e686ff..8717394, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 108/108, Architecture.Tests 7/7,
  others 1/1; mutation score 92.05%.
Task 9: minor (deferred): a single flaky Stryker timeout on a PeriodDefinition.cs mutant was seen once and
  did not reproduce (my own full run came back clean at 92.05%). Worth watching in CI.
Task 9: note — PeriodKey is a readonly record struct, so default(PeriodKey) bypasses Create's validation.
  Nothing constructs one today; the QuarterlyRange equivalence argument holds in practice. Structural note
  for phase 4+ when budgets start round-tripping keys through persistence.

=== PHASE 0 COMPLETE (Tasks 1-9) ===
Acceptance per the plan: `dotnet build -warnaserror`, `dotnet test` and `dotnet stryker` all pass;
architecture tests pass; the I9 tiling property test passes at 10,000 iterations per property. All met.
Task 10: review 1 — spec compliant, no Critical/Important. Reviewer verified enum values digit for digit,
  all four consistency rules as real runtime branches (each with a positive AND negative test), and that
  Liability is a genuine dead end pinned by an InlineData case rather than left accidental. The sibling-
  prefix trap is CLOSED: ChildPathPrefix => Path + "/", so /expense/gaming/ cannot match /expense/gaming-pc,
  and the implementer added a test for it beyond the brief (a `Path + "/"` -> `Path` mutant would otherwise
  have survived Stryker). Slugify traced against spaces, punctuation, accented (Étterem -> étterem, preserved
  not stripped), mixed case and punctuation-only-to-empty. duplicate_sibling_name correctly left unreachable
  here — Create takes a single parent, not a sibling set; Task 11 owns it.
Task 10: complete (commits 4ca01d9..486610a, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 139/139, Architecture.Tests 7/7, others 1/1.
Task 10: minor (deferred): Account.Restore and Account.UpdatePresentation have NO test — plain assignments,
  so a mutant flipping IsArchived or dropping an assignment would survive. CARRIED INTO TASK 16, which
  re-runs Stryker over the ledger core; this is the likeliest new survivor.
Task 10: minor (deferred): IsLegalCombination and ValidateName are surface not named in the brief's
  Interfaces bullet list, though present in its Step 4 reference code. Not implementer scope creep.
Task 10: minor (deferred): Account.cs:76 NameUnusable echoes the untrimmed name argument.
Task 11: review 1 — spec compliant, no Critical/Important. Reviewer hand-traced a 3-level chain
  (gaming -> steam -> deck) through both Rename and Move and confirmed RewriteDescendants is depth-generic
  (substring slicing on each descendant's own path in one pass, not a level-by-level walk), with tests
  pinning depth 2. Cycle prevention uses newParent.Path.StartsWith(account.ChildPathPrefix), depth-generic
  by construction. The duplicate check compares SLUGS, not raw names — i.e. what actually collides in Path.
  All validation completes before the first Apply* call in both methods, so no partial mutation on a
  rejected operation. Archived siblings still block slug reuse, consistently in both methods.
Task 11: complete (commits e029bf1..70a402a, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 152/152, Architecture.Tests 7/7, others 1/1.
Task 11: minor (deferred): Move duplicates Account.Create's root-vs-child path ternary. Both must agree with
  RootPathFor's no-trailing-slash contract independently.
Task 11: minor (deferred): account.currency_mismatch_with_parent on Move is unexercised (all fixtures use
  EUR). Branch reachability rests on inspection, not a test.
Task 11: minor (deferred): the cycle test only tries a direct child as target, not a grandchild. Mechanism
  is depth-generic by construction.
Task 12: review 1 — spec compliant. Reviewer read every line for sign manipulation and found NONE (no
  Math.Abs, no unary minus, no Negate, no display helper); -6000 round-trips unchanged. Enum values match
  digit for digit with all four members including the unused Import. 2 Important, both plan-mandated:
    F1 Posting.cs:38-41 — when the PERSISTED CurrencyCode does not resolve via Currency.FromCode, the
       mismatch path builds CurrencyMismatchException(currency, currency), i.e. "Cannot combine EUR and
       EUR amounts." Self-contradicting, and it hides the real defect (an unresolvable stored code) at
       exactly the moment a reader most needs a clear diagnostic. The ternary branch is also unexercised
       (both test currencies are valid), so a Stryker mutant flipping it survives.
    F2 Posting.cs:37 — AmountIn matches on `string.Equals(currency.Code, CurrencyCode)` while
       Money.RequireSameCurrency (Money.cs:62) uses full Currency record equality over Code AND
       MinorUnitExponent. Two types that must agree, disagreeing. Dormant today (only the three canonical
       singletons are ever used, and Task 4's fix means a KNOWN code can no longer carry a non-canonical
       exponent), but an unknown code created via Currency.Create with two different exponents would slip
       through and mislabel the minor-unit scale, corrupting ToDecimal().
  Ruling: fix both, together, because they are one incoherence with two faces — AmountIn tries to compare a
    rich Currency against a flattened string and has no coherent story when the string does not resolve.
    F1 has a live consequence at Task 16's Stryker re-run, and F2 is a one-line inconsistency between two
    types whose whole purpose is preventing mislabelled amounts. Constraint on the fix: a Posting whose
    stored code is legitimately outside the known table must NOT start throwing on correct calls, so an
    unresolvable stored code cannot simply become an error — it needs the ordinal-code fallback plus an
    exception that names the raw stored string instead of claiming "EUR and EUR".
    Cost if wrong: AmountIn gets slightly more code on a path nothing reaches yet.
Task 12: fix round 1/5 (2 addressed, 0 open — F1 the unresolvable branch now names the raw stored code
  (CurrencyMismatchException(CurrencyCode, currency)) and a test asserts Message contains "ZZZ" and does NOT
  contain "EUR and EUR"; F2 the resolvable path now uses full Currency record equality, matching
  Money.RequireSameCurrency; commits e3b14ee..530a932). RED evidence real for both: the pre-fix message
  literally read "Cannot combine EUR and EUR amounts." for a ZZZ-stored posting, and the pre-fix code
  silently accepted ZWD@2 vs ZWD@4.
  Ruling: ACCEPT the residual gap the implementer escalated — a same-code/different-exponent pair on an
    UNRESOLVABLE stored code cannot be rejected, because Posting persists only AmountMinor and a raw
    CurrencyCode string with no exponent, so there is no second data point to compare. The re-reviewer
    independently confirmed the load-bearing fact: Currency.Create returns the canonical instance for a
    known code and fails on a disagreeing exponent (Task 4's own fix), so two Currency values sharing a
    RESOLVABLE code can never disagree on exponent. Closing the literal scenario would mean persisting an
    exponent on Posting — a schema change the spec's Posting shape (5.3) does not have and which would
    ripple into Task 18. The implementer was right to write the honest test rather than force a pass.
    Cost if wrong: an unknown-currency import path in a later phase could mislabel scale; that path does
    not exist in phases 0-2 and would need its own design anyway.
Task 12: complete (commits 54d8cca..530a932, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 159/159, Architecture.Tests 7/7, others 1/1.
Task 12: minor (deferred): CurrencyMismatchException.Left widened from Currency to Currency?. No consumer
  exists today. FLAG FOR TASK 26 — the RFC 9457 Problem Details mapper must not dereference .Left blindly.
Task 12: note — the non-zero-amount rule is deliberately NOT enforced here; it lands in Transaction.Create
  (Task 13) and as the SQLite CHECK (AmountMinor <> 0) in Task 18.
Task 13: review 1 — spec compliant. Reviewer enumerated every construction path and confirmed the invariant
  boundary is CLOSED: Create is the only production entry point, _postings.AddRange runs only after
  BuildPostings succeeds, Posting's constructor is internal so no Posting can be obtained outside
  Money.Domain except through Transaction.Create, and Postings returns a fresh ReadOnlyCollection per
  access (immutable at the RUNTIME type level, not just the declared one). Per-currency grouping traced by
  hand: EUR+100/EUR-100/USD+50 IS correctly rejected. No sign flip. Overflow throws rather than wrapping,
  so no false zero.
  Ruling: ACCEPT the implementer's escalation that an overflow while summing throws OverflowException
    rather than returning a Result. Enumerable.Sum(IEnumerable<long>) accumulates checked (documented BCL
    behaviour), Money.Add/Subtract/Negate already throw unguarded under the same convention, and amounts
    near long.MaxValue minor units are not reachable by plausible input to a single-user loopback app.
    Treating it as programmer error matches the spec's rule. Cost if wrong: a crash instead of a 400 on an
    input no real user can produce.
  1 Important, plan-mandated:
    F1 TransactionCreationTests.cs — NO test exercises genuine cross-currency balancing. The one
       currency-ish test uses USD amounts against EUR-only accounts, so it trips CurrencyMismatchWithAccount
       and never reaches the GroupBy/balance code at all. A mutant collapsing GroupBy(p => p.CurrencyCode)
       to a constant key — merging all currencies into one global sum — survives all 13 tests. The plan
       itself names Transaction.BuildPostings as Task 16's highest-value Stryker target.
  Ruling: fix F1, and bundle the Minor vacuous-assertion finding below. F1 is a hole in exactly the place
    the plan says matters most, two tasks before the gate re-runs. Cost if wrong: two extra tests.
Task 13: fix round 1/5 (2 addressed, 0 open — F1 three new tests that genuinely REACH the per-currency
  balance code (accounts whose CurrencyCode matches each draft, so CurrencyMismatchWithAccount does not
  short-circuit first); F2 the vacuous `?.` assertion replaced with an explicit cast + NotBeNull +
  IsReadOnly.BeTrue; commits 9cd0289..1c8ccd9). Teeth proven: with GroupBy keyed to a constant, exactly the
  two intended tests fail and the other 14 stay green. The implementer added a third test beyond what I
  asked — Amounts_that_cancel_only_by_ignoring_currency_do_not_balance (EUR +10000, USD -10000) — which is
  the strongest of the set: the constant-key mutant would sum these to zero and wrongly ACCEPT.
  The "third test stayed green under the mutant" nuance is mathematically correct, not a broken test: two
  groups that each sum to zero also sum to zero under any grouping key. It guards a different mutant class
  (blanket rejection of multi-currency).
Task 13: complete (commits 11902f8..1c8ccd9, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 175/175, Architecture.Tests 7/7, others 1/1.
Task 14: review 1 — spec compliant, no Critical/Important. Reviewer traced Replace statement by statement:
  it calls the SAME internal BuildPostings that Create uses (no parallel validation that could drift), and
  all five mutating statements come strictly after `built.IsFailure` is known false, so a rejected Replace
  leaves the object bit-for-bit unchanged. Void never touches _postings, so voiding preserves the audit
  trail D11 exists for. UpdatedAtUtc is the last statement before every Ok and absent from every early
  return; CreatedAtUtc/BookedAtUtc have no write site outside the constructor. Void reason rejects both ""
  and whitespace via the catalogued code.
  Ruling: ACCEPT SetExternalRef having no IsVoided guard (the implementer flagged it for judgment).
    ExternalRef is pure correlation metadata — it touches no posting, amount or account reference, so it
    cannot violate I1, I2, I4 or I11, all of which are about postings and balances. Blocking it post-void
    would harm the reconciliation use case the field exists for: recording which external row a voided
    duplicate corresponded to, after the void. Cost if wrong: a voided transaction's external reference can
    still be corrected, which is the behaviour I want anyway.
Task 14: complete (commits 09b8dcf..b73c721, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 183/183, Architecture.Tests 7/7, others 1/1.
Task 14: minor (deferred): description validation is copy-pasted between Create and Replace rather than
  factored into a helper. Not an invariant-drift risk (those all route through BuildPostings).
Task 14: minor (deferred): Replace's ArgumentNullException.ThrowIfNull guards are untested. CARRIED INTO
  TASK 16 — Stryker will likely report these two lines as surviving mutants.
Task 15: review 1 — spec compliant, implementation correct on inspection: ZERO negation anywhere in
  BalanceCalculator (every line read), IsVoided filtered before touching postings in all three
  transaction-consuming methods, asOfInclusive genuinely inclusive while DateRange.Contains is genuinely
  half-open (no conflation), TotalSpending filters strictly on Kind == Expense (not Role, not path, not
  name), IsInSubtree uses ChildPathPrefix so /expense/gaming cannot absorb /expense/gaming-pc, mismatched
  currencies EXCLUDED rather than summed (deliberately bypassing Posting.AmountIn, which would throw —
  correct for a query context), all three sums checked(). LedgerGen genuinely produces all four shapes:
  Expense (groceries/fun vs bank), Income (bank vs salary), TransferToSavings and InvestmentPurchase (both
  Asset-only pairs, so structurally invisible to TotalSpending), with voiding an independent coin flip.
  Three of the five properties are non-tautological — the I3 one recomputes the expected value with an
  independent LINQ filter rather than by calling BalanceCalculator.
  Property iteration count is 2_000, which is exactly what the brief specifies (controller-verified).
  1 Important, plan-mandated:
    F1 TotalSpending ships with ZERO test coverage anywhere in the repo at this commit — no example test,
       no property. The plan defers its property test to Task 16. But this is the method that makes I12
       structural, in the task the brief itself calls "the reference implementation of every balance query
       in the system", and it currently rests on a one-time manual review plus a future task's promise.
  Ruling: fix it. Task 16's Stryker run is the very next task and an untested public method on the highest-
    value mutation target is the likeliest source of survivors; more importantly, every other method in this
    same file is test-first and this one is not. Cost if wrong: two or three example tests.
  Ruling: bundle the Minor currency-exclusion finding — LedgerGen is single-currency by construction, so the
    `posting.CurrencyCode != currency.Code -> continue` branch in all three methods is unverified by any
    test. One example test with a non-EUR posting closes it, and it is the same file. Cost if wrong: one test.
Task 15: fix round 1/5 (2 addressed, 0 open — F1 five TotalSpending example tests (transfer excluded,
  investment purchase excluded, voided expense excluded, half-open boundary pair with dates exactly on
  Start and exactly on EndExclusive, plus income excluded); F2 a cross-currency exclusion test that
  genuinely reaches the filter branch; commits efc796e..6df044b). Pure addition — no existing test,
  property, generator or iteration count touched, no src/ change.
  NOTE — the implementer CORRECTED MY INSTRUCTION and was right to. I told it to prove the tests' teeth by
  mutating `Kind != Expense` into `Role != Category`. That mutation is indistinguishable from the original
  for transfers and investment purchases: those post to Bank/SavingsPocket/Investment, and
  Account.IsLegalCombination makes Role=Category legal ONLY for Kind in {Income, Expense}, so no Asset-kind
  posting is ever Role=Category under either filter. Only an INCOME account discriminates, because Income
  and Expense both carry Role=Category. It verified this empirically, said so, and added
  Total_spending_excludes_income (fails under the mutation at -299000 = 1000 expense minus a wrongly
  included 300000 salary). The re-reviewer independently confirmed the legality table. My teeth-proof was
  a bad discriminator; reporting "the transfer tests fail under it" would have been false.
Task 15: complete (commits f350630..6df044b, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 200/200, Architecture.Tests 7/7, others 1/1.
Task 15: minor (deferred): LedgerGen's Build2 helper only ever makes exactly-2-posting transactions, so
  Every_generated_transaction_has_at_least_two_entries can only ever prove "always exactly 2".
Task 15: minor (deferred): SubtreeBalance is covered by example tests only, not by any property.
Task 16: review 1 — spec compliant, no Critical/Important. All four template signatures match character for
  character, and the postings were verified LEG BY LEG against the spec's worked-example table: Expense
  (+category/-paidFrom), Income (+receivedInto/-category), Transfer (+to/-from), OpeningBalance
  (+account/-equity). Sign wiring is centralised in one private Build helper rather than duplicated per
  template. I12 holds structurally — Transfer and OpeningBalance require Kind=Asset on every leg, so they
  can never touch a Kind=Expense account — and the property test computes its expected value with an
  INDEPENDENT LINQ filter rather than re-deriving it from TotalSpending, so it is not tautological.
  Diff stat: 699 insertions, 0 deletions across 8 files — no existing test weakened, deleted or skipped,
  and all four new properties run at iter: 2_000 as specified.
  CONTROLLER-VERIFIED SCORE: ran `dotnet stryker` from src/Money.Domain myself ->
  "[07:50:14 INF] The final mutation score is 93.81 %" (up from 92.05% at Task 9). BalanceCalculator.cs and
  Transaction.cs both at 100%. Threshold 80 untouched; no exclusion, no Stryker-disable pragma.
  Ruling: ACCEPT the implementer's scope widening (it flagged this itself). It closed every survivor in
    BalanceCalculator.cs, Transaction.cs and LedgerTemplates.cs rather than only the three thin spots I
    named. The brief's own Step 6 names Transaction.BuildPostings and BalanceCalculator as "the highest-
    value targets", so that work follows the brief rather than exceeding it, and LedgerTemplates.cs is this
    task's own new file. Cost if wrong: extra tests on the two files the plan cares most about.
  The reviewer independently re-derived every "equivalent mutant" claim against source rather than taking
  the label on faith — Sign <= 0 masked by Transaction.Create's own IsZero check, DescendantsOf's null
  guard masked by LINQ Where, IsLegalCombination's `_ => false` unreachable with all 7 roles covered,
  Slugify's pendingHyphen initial value dead. All four check out.
Task 16: complete (commits f289dd9..f9cbc5a, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  others 1/1; mutation score 93.81%.
Task 16: minor (deferred): TransactionVoidAndReplaceTests ToString test pins the "[voided]" debug-string
  format. Debug helper, not user-facing, asserts both branches — defensible but implementation-pinning.

=== PHASE 1 COMPLETE (Tasks 10-16) ===
Acceptance per the plan: I1, I2, I3, I4, I5, I11 and I12 all hold — I1, I3, I11 and I12 under CsCheck
property tests over generated ledgers containing transfers, savings funding and investment purchases.
dotnet test and dotnet stryker pass. Money.Domain still has zero external dependencies. All met.
Task 17: review 1 — spec compliant, no Critical/Important. All 10 port files, all 5 contract files and the
  mapper match the brief signature by signature. Ports are pure — no EF Core, IQueryable, DbContext or
  ASP.NET type in any signature or in any type they return. ToStored routes rounding through
  Money.RoundToMinor rather than a second helper (reviewer read the domain method, not just the report).
  IsNegatedForDisplay negates exactly Income/Liability/Equity and leaves Asset/Expense alone, and the
  direct theory test asserts all five kinds individually, so it WOULD catch Expense being added.
  ToDisplay/ToStored verified inverse by hand for negative income, zero, JPY (exponent 0) and EUR; the
  reviewer went further and confirmed Currency.MinorUnitScale is always an exact power of ten, so the
  inexact-division case cannot arise at all. DTO vocabulary clean: transaction fields are Lines /
  TransactionLineDto / Amount, never posting/debit/credit; categories are CategoryNodeDto.
  ApplicationAssemblyMarker.cs is genuinely deleted and DependencyRuleTests now anchors on IUnitOfWork.
  CONTROLLER-VERIFIED: grepped all of src/ for `* -1`, Math.Abs and unary minus on an amount — NO hit
  outside DisplayAmountMapper. The one-mapper rule holds as of this commit.
Task 17: complete (commits d357ad8..c152c5e, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 15/15, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1.
Task 17: minor (deferred): DisplayAmountMapper negates a long without a checked context, while the domain's
  own Money.Negate uses checked(-AmountMinor). Only reachable at long.MinValue (~92 quintillion minor
  units), so no practical impact, but inconsistent with the codebase's own convention.
Task 18: implementer returned DONE_WITH_CONCERNS with four disclosed items. Controller rulings:
  Ruling (MY ERROR, reverting): I told the implementer "enums are persisted as integer values — the domain's
    enum numbers are a storage contract". That is NOT a spec rule and it CONTRADICTS the plan. The brief's
    own Task 18 code uses .HasConversion<string>() for Kind, Role and SourceKind (brief lines 316-317, 355)
    and filters the idempotency index with SourceKind IN ('Recurring','Accrual') (line 374) — which is
    verbatim what spec section 8 writes. The spec's storage rules cover amounts (INTEGER), rates
    (NUMERIC/TEXT) and the ban on REAL; they say nothing about enums. The implementer followed my
    instruction over the brief and rewrote the CHECK/filter SQL and test literals accordingly.
    I checked whether downstream depends on either choice: Task 20's queries compare typed enums in LINQ
    (a.Kind == k) and Task 25 exports SourceKind.ToString(), so both are storage-agnostic — there is no
    cascade either way. With the tie broken on the merits: strings are what the plan and spec say, they are
    legible when the user opens their own SQLite file by hand (a real benefit for a local personal-finance
    app), and they make the enum NAMES the storage contract rather than the numbers, which is the safer of
    the two against a future renumber. Reverting to the plan's string storage.
    Cost if wrong: slightly larger enum columns in a single-user database.
  Ruling: ACCEPT Postings and IdempotencyRecords not carrying CreatedAtUtc/UpdatedAtUtc. The spec conflicts
    with itself here — section 8 says "every table has" them, but section 5.3 defines Posting as
    (Id, TransactionId, AccountId, AmountMinor, CurrencyCode, Memo?) with no timestamps, and the brief's
    type shapes follow 5.3. Postings are immutable children of a transaction that carries its own
    timestamps, so per-posting audit columns would be redundant; adding them would mean changing a DOMAIN
    type to satisfy persistence, which the layering rule forbids. Cost if wrong: a later migration adds two
    columns to a child table.
  Ruling: ACCEPT the fix for the plan defect at brief lines 367 and 371 — two HasIndex calls over the same
    columns silently collapse into one index in EF. Naming both at creation is correct and necessary.
  Ruling: ACCEPT the directory-scoped .editorconfig exempting IDE0161/CA1861 for GENERATED migration code.
    Same pattern as Task 1's tests/.editorconfig, narrowly scoped to the scaffolder's own output, and the
    alternative is hand-editing generated files on every migration. Cost if wrong: two analyzer rules are
    silent inside one generated directory.
Task 18: review 1 — spec compliant. Reviewer checked spec section 8's constraint table row by row against
  the generated DDL: all present and correctly named. Two findings of note. The CK_Accounts_KindRole
  predicate is a semantic match to Account.IsLegalCombination including the non-obvious case (Kind=Liability
  rejected identically by domain and by CHECK). The partial idempotency index is genuinely partial: Manual
  rows never enter it so two same-day manual entries succeed, while two Recurring rows sharing
  (SourceId, OccurredOn) collide — both directions tested. All 8 (now 9) SchemaConstraintTests insert via
  raw ADO.NET on the fixture connection, never through EF, so a pass is real evidence about the SCHEMA.
  The string-enum revert was verified clean across configuration, check constraint, index filter, migration,
  designer, snapshot and test literals, with no integer remnants. Domain untouched; _postings mapped via
  HasField/PropertyAccessMode.Field. CONTROLLER-VERIFIED: grepped src/Money.Infrastructure for REAL — none.
  1 Important, plan-mandated:
    F1 Posting.AccountId had NO foreign key to Accounts — only FK_Postings_Transactions_TransactionId
       existed. The database would silently accept a posting pointing at an account that does not exist.
       Neither spec section 8 nor the brief's PostingConfiguration template declares the relationship.
  Ruling: fix it. This task's own argument is that constraints live in the database because the domain
    cannot see concurrent writers; a dangling account reference on a posting is exactly what a foreign key
    is for. Restrict (not Cascade) is required because deletes do not exist and Restrict is what stops an
    account deletion cascading into ledger history. Cost if wrong: one more constraint in a schema whose
    whole purpose is refusing bad data.
Task 18: fix round 1/5 (enum storage reverted to strings — MY error, see ruling above; commits 54a1980..23280c5)
Task 18: fix round 2/5 (1 addressed, 0 open — F1 FK added with ReferentialAction.Restrict, migration
  genuinely REGENERATED not hand-patched (the .cs, .Designer.cs and snapshot all carry the block, and Up()'s
  CreateTable order plus Down()'s drop order were re-topologically-sorted for the new dependency, which a
  hand-patch would not replicate); commits 23280c5..9de0380). SqliteFixture already had Foreign Keys=True,
  verified in code — so the new test's FK really is enforced.
Task 18: complete (commits 96419ab..9de0380, review clean after 2 fix rounds)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 24/24, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1.
Task 18: minor (deferred): CK_Accounts_PathShape (Path LIKE '/%') is an implementer addition beyond spec
  section 8's table. Harmless and consistent with Account.RootPathFor.
Task 18: minor (deferred): the migrations-directory .editorconfig must be remembered if the migrations
  folder is ever renamed.
Task 19: review 1 — interfaces match the brief exactly. DataDirectory.Resolve has NO path that can reach
  AppContext.BaseDirectory or anything executable-relative (whole file read), so D10 holds at the unit
  level. The interceptor applies all three pragmas on both ConnectionOpened and ConnectionOpenedAsync — i.e.
  every connection, not once at startup. Backup-before-migrate is strictly linear and FAILS CLOSED: an
  exception from CreateBackupAsync propagates and MigrateAsync never runs. Test hygiene clean: no test
  touches the real %APPDATA%, no test mutates MONEYAPP_DATA_DIR at all (so nothing can leak across xUnit
  parallel collections), and the temp-dir test clears the SQLite connection pool before deleting to handle
  Windows file locks.
  Ruling: ACCEPT the implementer's added SqlitePragmaInterceptorTests beyond the brief. The brief's own
    Foreign_keys_are_on_for_every_connection test only reads a pragma baked into SqliteFixture's connection
    STRING, so it passes whether or not the interceptor works. The added test opens a real FILE-backed
    connection (journal_mode is meaningless on :memory:) and reads all three pragmas back, with a real RED
    from breaking the pragma string. That closes a hole rather than adding decoration.
  Ruling: ACCEPT the [LoggerMessage] conversion — the brief's literal LogInformation calls fail CA1848 under
    this repo's warnings-as-errors. Behaviour, levels, messages and call sites unchanged.
  2 Important, both plan-mandated:
    F1 DatabaseInitializer.InitialiseAsync's backup-before-migrate orchestration has NO test — the brief's
       own DatabaseInitializerTests never instantiates the class. This is the task's defining safety
       property and it currently rests entirely on code inspection.
    F2 A_freshly_migrated_database_passes_the_integrity_check runs SUM(AmountMinor)<>0 against a database
       with ZERO postings, so it passes trivially whether or not the schema enforces anything. The brief's
       own stated intent is "apply all migrations from empty, SEED, apply again" — the seed step is missing.
  Ruling: fix both. A backup taken after a destructive migration is worthless, and an integrity test with no
    data cannot fail. The implementer had everything it needed for F1 within this task's scope (it had just
    built a comparable live-connection test for the interceptor). Cost if wrong: two more tests on the path
    that protects the user's only copy of their ledger.
Task 19: CARRY INTO TASK 26 — SqlitePragmaInterceptor is referenced nowhere in src/ except its own
  definition. It is not yet registered on any real DbContextOptionsBuilder, so in the running app the
  pragmas do NOT yet apply. The composition root must wire it, or Task 18's foreign keys are inert.
Task 19: CARRY INTO TASK 26 — nothing calls Directory.CreateDirectory, so first run against a nonexistent
  %APPDATA%\MoneyApp depends on the composition root creating it.
Task 19: fix round 1/5 (2 addressed, 0 open — commits 648a9a9..c62d8ff). F1: the ordering test genuinely
  DISCRIMINATES rather than counting calls — RecordingBackupService checks TableExists(connection,
  "Accounts") at the moment CreateBackupAsync is invoked, against the same live connection the context
  migrates, so asserting it was False at backup time and True after InitialiseAsync is a causal check. The
  fail-closed test asserts the Accounts table does NOT exist afterwards, so it proves the migration never
  ran rather than only that an exception propagated. Brand-new-database behaviour pinned as found:
  backup is SKIPPED when no migrations have been applied (alreadyApplied.Any() guard). F2: the integrity
  test now seeds two accounts, a transaction and two postings (+2000/-2000) via raw ADO.NET through the real
  schema; teeth proven by a temporary UPDATE ... AmountMinor + 1 that flipped the check from 0 to 1, and the
  re-reviewer searched the diff and confirmed no trace of that corruption was committed.
Task 19: complete (commits 4ff49b0..c62d8ff, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 37/37, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1.
Task 19: minor (deferred): DataDirectory does not reject a relative-path override (MONEYAPP_DATA_DIR=.\data
  resolves against the process CWD, which differs between the desktop host and a future server host).
Task 20: review 1 — spec compliant, no Critical/Important. All seven ports implemented, verified against the
  actual interface FILES rather than the brief's sample. The invariant-critical semantics all check out
  against BalanceCalculator line for line: voided excluded in every balance query (LedgerQueries.cs:11,26,40
  vs BalanceCalculator.cs:34,63); subtree prefix carries the trailing slash and includes the root
  (path == p || StartsWith(path + "/")), with a dedicated sibling-lookalike test; asOfInclusive uses <= and
  the window uses >= from / < to, the exact half-open translation of DateRange.Contains; the spending
  selector uses Kind == AccountKind.Expense, NOT Role; archived accounts are never filtered out.
  The agreement test is a genuine TWO-PATH comparison, not a tautology: fromSql runs LINQ-to-Entities
  against real SQLite while fromDomain is a pure fold over the same in-memory transaction list, and neither
  component calls the other. It runs over 25 independently generated ledgers per test, and LedgerGen was
  confirmed to produce all four shapes plus independent per-transaction voiding.
  IntegrityChecker genuinely CAN fail — two tests corrupt a healthy seeded ledger via raw SQL bypassing the
  domain and assert IsHealthy == false.
  Paging is textbook keyset pagination: OrderByDescending(OccurredOn).ThenByDescending(Id) with a matching
  cursor predicate, and Id is a Guid v7 so the tiebreaker is deterministic for same-day transactions.
  CONTROLLER-VERIFIED: no FromSqlRaw/ExecuteSqlRaw anywhere in Money.Infrastructure — the only raw
  CommandText is the pragma constant — so the SQL-injection concern is moot; the query layer is LINQ.
  Controller resolution of the reviewer's one ⚠️: ILedgerQueries.BalanceOfAsync/SubtreeBalanceAsync take no
    Currency parameter, so the SQL path does no currency filtering while BalanceCalculator does. NOT a gap.
    Transaction.BuildPostings rejects a posting whose currency differs from its account's, and Account.Create
    rejects a child whose currency differs from its parent's, so an account and its whole subtree are
    structurally mono-currency and the filter is a no-op for any data the domain can create. This is a
    Task 17 port-shape decision, not a Task 20 defect. Resolved, not deferred.
Task 20: complete (commits 950b076..1bfb8cb, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 49/49, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1.
Task 20: minor (deferred): ListAsync's projection calls context.Accounts.First(...) twice per posting line,
  a correlated subquery per posting rather than a join. Correct but wants a join before production-sized lists.
Task 20: minor (deferred): the paging test runs one random ledger rather than the SampleLedgers(25) pattern,
  so same-date tie-breaking is only stress-tested by whatever one draw produces.
Task 20: minor (deferred): worth a comment at BalanceOfAsync/SubtreeBalanceAsync noting the currency filter
  is intentionally absent because the domain enforces mono-currency subtrees.
Task 21: review 1 — spec compliant, no Critical/Important from the reviewer. Categories confirmed as SUGAR
  over accounts: CreateCategoryHandler calls Account.Create with AccountRole.Category, GetCategoryTree reads
  ListAsync(..., Role.Category, ...) — one mechanism, no parallel tree and no category table. Patch loads
  ChildrenOfAsync and DescendantsOfAsync BEFORE calling AccountTree.Rename/Move, so the domain receives
  parameters rather than ports. GetAccountBalance routes through DisplayAmountMapper.ToDisplay exactly once;
  no sign flip anywhere in the Accounts, Categories or Mapping namespaces. All failures return
  DomainErrors.Account.* factories; zero inline error construction. No double-entry vocabulary in any DTO
  field or user-visible message. One SaveChangesAsync per use case.
  Ruling: ACCEPT SortOrder = 0 for new categories. The brief's literal handler code says
    UpdatePresentation(siblings.Count, ...) but the brief's OWN test creates "Zoo" then "Apples" and expects
    ["Apples", "Zoo"]; with siblings.Count that yields ["Zoo", "Apples"] and the test fails. A genuine
    self-contradiction in the plan, and the test is the better source of truth: new siblings default to
    alphabetical order, and explicit reordering is a later feature. Cost if wrong: new categories sort by
    name until someone sets SortOrder deliberately.
  Ruling (ESCALATED past the reviewer's "not this task's responsibility"): the non-cascading archive is a
    real Important defect in THIS task's code, not a UI question for Tasks 27/31. I read
    GetCategoryTreeHandler myself: BuildChildren starts at Guid.Empty and only recurses into parents that
    are present in the filtered set. So archiving "Gaming" with includeArchived:false leaves
    "Gaming -> Steam" in byParent[GamingId] with nothing ever recursing into it — the whole active subtree
    SILENTLY DISAPPEARS from the category tree, while the flat ListAsync still reports those children as
    active. Two application-layer readers disagree about the same data, and a category holding real
    transactions becomes invisible with no way to see or fix it from the tree.
    Decision: refuse to archive an account that still has non-archived descendants, with a catalogued error
    telling the user to archive the children first. That keeps Account.Archive's single-node semantics (no
    cascade, so no ambiguity about which children to restore later), makes the inconsistent state
    unreachable rather than merely unrendered, and is fully reversible. Rejected the alternatives: cascading
    makes un-archiving ambiguous, and filtering descendants at read time only hides the symptom while the
    flat list keeps contradicting the tree. This adds a rule the plan does not name, which I would normally
    avoid — but the spec's "archive, never delete" cannot mean "archive, and silently make unreachable".
    Cost if wrong: archiving a parent becomes a two-step operation the user must do deliberately.
Task 21: fix round 1/5 (1 addressed, 0 open — commits 524518f..71cc076). ArchiveAccountHandler now loads
  DescendantsOfAsync(account.ChildPathPrefix) and refuses with DomainErrors.Account.HasActiveDescendants if
  any descendant is unarchived, at any depth. The error factory lives in the catalogue (DomainErrors.cs:86-89),
  used from there, never constructed inline. Message is user-facing and actionable: "'{name}' still has
  active {categories|accounts} underneath it. Archive those first." — no double-entry vocabulary, and it says
  "categories" when the thing is a category. Account.Archive's single-node domain semantics are UNCHANGED;
  the rule lives in the handler. Five tests: leaf succeeds, parent with active child refused, parent whose
  only descendant is already archived succeeds, a "Gaming PC" sibling does NOT block archiving "Gaming"
  (pinning ChildPathPrefix), and a tree/flat-list agreement test.
Task 21: complete (commits a57ff21..71cc076, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 69/69, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1. Error code confirmed at DomainErrors.cs:87, not inline.
Task 21: minor (deferred): BuildChildren returns concrete CategoryNodeDto[] rather than IReadOnlyList<T> for
  a private helper, to satisfy CA1859 under warnings-as-errors.
Task 22: review 1 — spec compliant, NO findings at any severity. All five handlers plus the mapper match the
  brief's signatures. Display-amount round trip proven in BOTH directions including a negated kind:
  An_income_line_typed_as_a_positive_number_is_stored_as_a_credit types 3000m for Income and asserts it is
  stored as -300_000, and the read-back test returns 20.00m unchanged through ToDisplay. No invariant
  re-implemented in a handler — the domain's Create/Replace/Void still own I1, I2 and I4. SaveChangesAsync
  once per handler, after validation, with failed domain calls returning early. All failures catalogued
  (TooFewPostings, AccountUnknown, NotFound plus the domain's own). No ambient time. No new sign flip.
  Ruling: ACCEPT the ValueGeneratedNever() fix on Posting.Id, which the implementer found via TDD. The
    reviewer confirmed all three of its claims: EF's default convention for a Guid key is
    ValueGeneratedOnAdd, so a non-default client-supplied Guid discovered through a collection navigation is
    classified Modified rather than Added — which is why Replace threw DbUpdateConcurrencyException, since
    Replace clears and repopulates the postings collection; ValueGeneratedNever() is the correct declaration
    rather than a mask, because the domain generates keys with Guid.CreateVersion7; and it changes only the
    change-tracker's behaviour, not the DDL (Postings.Id was already bare TEXT with no default), so no
    migration is needed. Good catch — this would have surfaced as a runtime failure in Task 28's endpoints.
Task 22: complete (commits 78310d9..aeffa09, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 78/78, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1.
Task 22: CORRECTION to an earlier ledger claim — my Task 17 note said no Math.Abs exists anywhere in src/.
  That was true then; Task 20 introduced one at LedgerQueries.cs:127, inside
  OrderByDescending(l => Math.Abs(l.AmountMinor)) to rank lines by magnitude when choosing a headline line.
  That is a ranking comparison, not a display negation, so the one-mapper rule still holds — but the
  blanket "no Math.Abs in src/" phrasing was stale and is corrected here.
Task 23: review 1 — spec compliant, NO findings at any severity. Both handlers are pure coordinators: they
  load accounts, call LedgerTemplates.Expense / LedgerTemplates.Transfer, and save — no PostingDraft list
  built by hand, so no second copy of the sign convention. Money.RoundToMinor called once per handler; no
  second rounding path. All failures use catalogued DomainErrors factories. No double-entry vocabulary in
  either handler or TodayResolver; descriptions read as plain account and category names.
  The midnight-boundary test DISCRIMINATES: the clock is set to 23:30 UTC on Aug 31, which is 01:30 Budapest
  on Sep 1, no date is supplied, and OccurredOn is asserted to be Sep 1 — that assertion fails if the code
  resolves today from UTC instead of the configured zone.
  The I12 transfer guarantee is proven structurally, not by post-hoc exclusion: a transfer between two asset
  accounts leaves the /expense subtree balance at zero, and a transfer into a category is rejected with
  account.kind_role_mismatch.
  Ruling: ACCEPT the IAsyncLifetime Task-vs-ValueTask deviation — mechanical, forced by this repo's xunit
    2.9.2 pin (the brief's signature is xunit v3). No behavioural difference.
Task 23: complete (commits e6eef98..bdf215e, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 87/87, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1.
Task 24: review 1 — spec compliant, no Critical/Important. Opening balance IS a real transaction through
  LedgerTemplates.OpeningBalance, with the Equity/OpeningBalance account ensured first; proven by
  The_opening_balance_is_not_spending_and_the_books_balance, which asserts the expense subtree is zero AND
  all balances sum to zero (I1). Starter categories go through Account.Create with Role.Category — the same
  factory as any other category, no bulk-insert shortcut. All nine spec names match exactly.
  Idempotence is enforced at ENTRY (FirstRunCompleted check returns Settings.AlreadyInitialised before any
  validation or write), and the half-seeded-then-reports-success failure mode I asked about is NOT reachable:
  every validation runs before the first accounts.Add(), and there is exactly ONE SaveChangesAsync, so a
  failure leaves nothing written — proven by A_bad_anchor_day_aborts_first_run_without_creating_anything.
  Anchor day 31 rejected with period.anchor_day_out_of_range and a message containing "not exist in every
  month" — not clamped. Unknown IANA zone rejected with period.unknown_time_zone, not thrown.
  No vocabulary leak: the equity account is named "Opening balance", not "Equity:OpeningBalance".
Task 24: complete (commits 386d278..f57e5fa, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 98/98, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1.
Task 24: minor (deferred): SeedCategories does `if (created.IsFailure) continue;` — a silently swallowed
  failure in a seed loop. Unreachable in practice (hardcoded valid names, pre-validated currency,
  Expense+Category is a legal combination), which is why it stays Minor rather than Important, but it is
  dead defensive code that would hide a real problem if the inputs ever stopped being hardcoded.
Task 24: minor (deferred): the Equity/OpeningBalance account is created even when the opening balance is
  zero (the transaction is correctly skipped). Harmless; the zero case is untested.
Task 25: review 1 — spec compliant. Backup uses source.BackupDatabase(destination) — the Online Backup API,
  not File.Copy. A test genuinely OPENS the produced file as a fresh SQLite database and runs SELECT COUNT(*),
  so it would fail on a corrupt or truncated backup rather than passing on mere existence. Retention keeps the
  newest N by sortable-timestamp filename ordering. The timestamp comes from clock.UtcNow. Exports emit raw
  AmountMinor with NO negation, so a re-import cannot double-negate. No test touches the real %APPDATA% —
  all use Path.GetTempPath() with a per-instance GUID subdirectory.
  1 Important (implementer-flagged):
    F1 SqliteBackupService called SqliteConnection.ClearAllPools() in PRODUCTION to fix a real Windows
       file-lock bug (Microsoft.Data.Sqlite pools native handles, so the disposed destination connection kept
       an OS lock on the just-written file and retention's File.Delete threw IOException). Correct fix, wrong
       blast radius: ClearAllPools is process-wide and would force reconnects for unrelated operations on
       every backup.
  Ruling: narrow it rather than accept or remove it. The lock problem is real, so "delete the call" was never
    an option; the objection was scope. Pooling=False on the DESTINATION connection string keeps that
    connection out of the pool entirely so its handle closes on dispose, leaving every other connection's
    pool untouched. Cost if wrong: one un-pooled connection per backup, which is the cheapest possible place
    to pay it.
Task 25: fix round 1/5 (1 addressed, 0 open — Pooling=False on the destination alone sufficed and
  ClearAllPools is fully gone from production, surviving only in two explanatory comments and in the two test
  teardowns where it belongs; commits 6fcafd0..01b1cb7). The retention test still exercises the File.Delete
  path that was throwing (5 backups, retention 3, four deletions) and passes with the broad call removed.
  Source connection correctly left pooled — retention only ever deletes money-*.db backups, never money.db.
Task 25: complete (commits 8519280..01b1cb7, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Application.Tests 104/104, Domain.Tests 241/241,
  Architecture.Tests 7/7, Api.Tests 1/1.
Task 25: minor (deferred): two backups within the same second produce the same filename and the second
  overwrites the first. In the brief's own code; both files would hold identical data and retention keeps the
  newest N regardless, so the harm is negligible.
Task 25: CARRY INTO TASK 26 — ExportLedgerHandler's Func<string, IExportService?> resolver still needs DI
  wiring; expected per the brief.
Task 26: review 1 — spec compliant. All FOUR carried-forward gaps are genuinely wired, not merely mentioned:
  SqlitePragmaInterceptor registered on the production DbContextOptionsBuilder (DependencyInjection.cs:63),
  Directory.CreateDirectory called before the connection string is built (:57), the export resolver maps both
  json and csv and returns null for an unknown format (:86-88), and nothing dereferences
  CurrencyMismatchException.Left — DomainErrorResults maps only DomainError/Result, with exceptions falling
  through to UseExceptionHandler so an unhandled exception cannot be reported as a validation problem.
  Money.Api still has NO direct project reference to Money.Domain. No double-entry vocabulary in any
  Problem Details title or detail.
  Ruling: ACCEPT the plan-mandated RED state. Brief Step 8 says verbatim "Leave them red and move on — that
    is the next task's starting condition", so this commit ships with Money.Api.Tests at 3 passed / 3 failed
    while every other project is green. Recorded honestly: THIS COMMIT IS NOT GREEN. Task 27 must turn those
    three account tests green and I will verify it. Cost if wrong: one commit in history where CI would fail;
    the branch is merged as a whole and the head after Task 27 is green.
  Ruling: ACCEPT the enum-registration fix (the brief's AddSingleton(mode) does not compile for a value type;
    the non-generic AddSingleton(typeof(HostingMode), mode) overload registers the same singleton) and the
    CancellationToken.None substitution (the brief used xUnit v3's TestContext.Current.CancellationToken; the
    repo pins v2.9.2).
  Ruling: ACCEPT the ApiFactory sandbox, which goes beyond the brief's literal code. The brief's code as
    given EMPIRICALLY created a directory at the developer's real %APPDATA%\MoneyApp on every test run — the
    exact hazard I warned about. The static constructor redirects MONEYAPP_DATA_DIR to a per-run GUID
    subdirectory under %TEMP% before any app instance can boot; the CLR guarantees it runs once per process
    before first type use, so there is no race under xUnit parallelism. Good catch.
  1 Important, plan-mandated, NOT entering a fix round:
    F1 TransactionEndpoints.cs maps a live placeholder GET /transactions returning 501. It exists only to
       satisfy the brief's own Step 8 assertion that the OpenAPI document contains "/api/v1/transactions",
       so it cannot be removed now without breaking a plan-mandated test.
  Ruling: carry it rather than fix it. The stub must exist until Task 28 owns that file. Instead of a fix
    round I am (a) instructing Tasks 27, 28 and 29 to REPLACE rather than extend the stub files, and
    (b) adding an explicit check at Task 33's acceptance that no 501 placeholder survives anywhere.
    Cost if wrong: a 501 endpoint ships; the Task 33 check is the backstop.
Task 26: complete (commits af0f998..94e49f6, review clean, no fix round; Api.Tests intentionally 3/6 red)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  Application.Tests 104/104; Api.Tests 3 passed / 3 failed by design.
Task 26: minor (deferred): the ApiFactory temp-directory cleanup is a best-effort ProcessExit handler, so a
  killed test process can leave %TEMP%\MoneyApp.Tests\<guid> behind.
Task 27: review 1 — spec compliant, no Critical/Important. All nine spec section 9 routes present with the
  right verbs, paths and query parameters. Endpoints are uniformly THIN: every one is parse -> call handler
  -> map Result via DomainErrorResults, with no validation, sign handling or business decision in an endpoint
  file. The three stub files were REPLACED, not extended, and TransactionEndpoints.cs and AdminEndpoints.cs
  are absent from the diff entirely — the 501 placeholder survives untouched for Task 28, as intended.
  Tests assert real HTTP behaviour: 201 with a Location header, 204, 400, 404, 409, Problem Details content,
  and round-tripped values — not merely 200. No vocabulary leak in any route segment, field name or message.
  THE RED STATE FROM TASK 26 IS RESOLVED: controller-verified whole suite green, Api.Tests 14/14, 366 total.
  Ruling: ACCEPT 409 Conflict for archive-blocked-by-active-descendants. The request is well-formed and the
    account exists; the refusal is an application-state conflict, which is what 409 is for — not 400
    (malformed) and not 422.
  Ruling: ACCEPT the substring match on "_blocked_by_" in DomainErrorResults.StatusCodeFor. I was wary of
    coupling a status code to a fragment of an error-code string, but it follows the pattern already
    established for ".duplicate_", the fragment is specific enough that an accidental collision is
    implausible, and the alternative — hardcoding each code — would need editing every time phases 3-8 add a
    state-conflict error. Cost if wrong: a future code containing that fragment gets 409 when it wanted
    something else, which is a visible wrong status rather than silent data damage.
Task 27: complete (commits 701b2f7..484424e, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  Api.Tests 14/14, Application.Tests 104/104 — 366 total, whole suite green.
Task 28: NOTE — the original implementer was cut off by an API rate limit after writing the code but before
  committing. Work was intact and uncommitted; I verified the gates myself, then dispatched a FRESH
  implementer to review the inherited tree, add coverage, commit and report. SendMessage (agent resume) also
  became unavailable at this point, so every later fix round dispatches a fresh agent with full context.
Task 28: review 1 — spec compliant. All seven spec section 9 routes present with correct verbs, paths and
  query parameters; the filter is applied to exactly the three POSTs that create money. The 501 placeholder
  is gone (controller-verified). Endpoints are thin — every route delegates to an Application handler and
  only maps Result to HTTP. No double-entry vocabulary; DTOs still say Lines. No sign flip in an endpoint,
  and Income_posted_as_a_positive_number_reads_back_as_a_positive_number pins that. IdempotencyFilter takes
  clock.UtcNow injected, not ambient. The key idempotency test asserts LEDGER STATE
  (balance.Should().Be(958m, "the retry must not post a second expense")), not just the HTTP response — so
  it would fail if a duplicate were silently written.
  4 Important findings. Rulings:
    F1 RACE WINDOW — the handler commits its transaction BEFORE the filter records the key, so two truly
       concurrent POSTs sharing an Idempotency-Key can both create a transaction. Task 18's partial UNIQUE
       index is filtered to SourceKind IN ('Recurring','Accrual'), so it does NOT cover Manual transactions —
       nothing at the database level catches this. Plan-mandated (the brief's filter code has no
       pre-reservation).
    Ruling: FIX IT. This is the one outcome this codebase treats as worst — silently duplicated money, with
      no constraint to catch it, in the exact feature the spec justifies as protection against retries. It is
      not hypothetical: Task 30 adds an HTMX quick-add row, and one double-click fires two near-simultaneous
      POSTs from a single browser. The reviewer costs the fix at 3-5 lines (reserve the key before the
      handler, catch the unique-constraint violation), which is not a redesign. Cost if wrong: a slightly
      more complex filter on three routes.
    F2 BODY MISMATCH — the same key replayed with a DIFFERENT body silently returns the original response.
       Plan-mandated (no body hash in the brief's code).
    Ruling: FIX IT, in the same round. Reserving the key already creates the place to store a body hash, and
      returning 409 on mismatch turns a silently wrong answer into a loud one. Cost if wrong: a client
      reusing a key carelessly now gets an error instead of a stale success.
    F3 No test proves the same key on DIFFERENT endpoints is a different operation, though the code keys on
       (key, endpoint).
    F4 Void error cases untested — no test for voiding a nonexistent or already-voided transaction.
    Ruling: bundle F3 and F4 into the same round; both are small and the files are already open.
Task 28: fix round 1/5 (4 addressed, 0 open — commits e0fd82a..4e1079e). F1: TryReserveAsync now inserts and
  commits the reservation row BEFORE next(context) runs, so a concurrent loser gets Reserved:false and returns
  409 idempotency.request_in_progress WITHOUT ever invoking the handler — it cannot create a transaction on
  any path. The reservation is gated by the existing composite primary key (Key, Endpoint), and the store
  catches only SqliteErrorCode 19 (SQLITE_CONSTRAINT) rather than swallowing every DbUpdateException. The
  classic reserve-before-run regression is handled: the filter releases the reservation both when the handler
  throws and when it returns a non-2xx, so a failed first attempt does not permanently lock out a legitimate
  retry. F2: the body hash is taken from context.Arguments[0] — the already-model-bound DTO — so the
  read-the-request-stream-twice bug is structurally impossible rather than merely avoided; mismatch returns
  409 via a new DomainErrors.Idempotency catalogue entry, not an inline code. F3 and F4 both tested as asked.
  On the flaky RED: the implementer disclosed that F1's test reproduced RED only ~1 in 10-20 runs against the
  old code rather than claiming a clean single-run RED. That is the right call — a genuine race cannot be
  made to fail deterministically without fault injection — and the evidence was real: a 16-way probe showing
  500x15/201x1 with a -84 balance move (a duplicate hidden behind an error response), plus a directly
  reproduced FAIL of the committed test at balance 916 instead of 958. Post-fix the test passes by
  construction (a database constraint gates the handler), not by scheduling luck.
  The reviewer agreed the shared-SQLite-connection contention in ApiFactory is pre-existing and out of scope,
  and that deleting the throwaway 16-way probe rather than keeping it as a permanent flaky test was correct.
  CONTROLLER-VERIFIED: ran Money.Api.Tests twice, 27/27 both times — no flakiness observed.
Task 28: complete (commits 579dbbf..4e1079e, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  Api.Tests 27/27, Application.Tests 104/104 — 379 total, whole suite green.
Task 28: minor (deferred): IdempotencyStore.TryReserveAsync's catch branch assumes the existing row is always
  found after a PK collision. If the WINNER's handler fails and releases the row in the narrow window before
  the loser's follow-up SELECT, the loser reads null and reports 409 "key reused with different payload" —
  a misleading code for a reservation that simply vanished. No data risk; a later retry succeeds cleanly.
Task 28: CARRY INTO TASK 33 — confirm no HTTP 501 placeholder survives anywhere (it is gone from
  TransactionEndpoints.cs; AdminEndpoints.cs is Task 29's).
Task 29: review 1 — spec compliant, ZERO findings at any severity. All three routes present with the right
  verbs and query parameter. The last stub is fully replaced. Content types are right and distinct:
  application/json with money-export.json, text/csv with money-export.csv, via Results.File so
  Content-Disposition is set. An unknown or missing format returns a Problem Details 400, not an unhandled
  exception. Nothing in the endpoints negates, reformats or rounds an amount — the handler returns a string
  and the endpoint only UTF-8 encodes it, so the export stays re-importable. Route segments and user-visible
  messages are free of double-entry vocabulary, while the export payload keeps the data's own vocabulary as
  intended.
  Ruling: ACCEPT the seeded-transaction test deviation as a STRENGTHENING. The brief's vocabulary test
    asserted the export "contains entries" against an EMPTY ledger, where the string appears nowhere
    regardless of naming — it would have passed for a broken export. Seeding one real transaction makes the
    assertion mean what it claims. The implementer also correctly rejected the alternative (renaming the
    top-level transactions key), which would have broken Task 25's tests.
  CONTROLLER-VERIFIED: grepped src/Money.Api/Endpoints for "501" — none. All five of Task 26's placeholder
  stubs are now real endpoints, which closes the carry I opened at Task 26.
Task 29: complete (commits f824b2b..b09496e, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  Api.Tests 32/32, Application.Tests 104/104 — 384 total, whole suite green.
Task 30: review 1 — spec compliant. All five quick-add requirements met: date defaults to today via
  TodayResolver with the injected IClock (never DateTime.Now in a Razor file), amount autofocused, Enter
  saves through the single form's submit button, account selection persists, category picker present.
  CONTROLLER-VERIFIED vocabulary sweep over src/Money.Api/Pages/ and app.css for
  posting|debit|credit|double-entry|ledger — ZERO hits. This is the first task with real views, so that
  matters. HTMX genuinely vendored (local file, version comment, no CDN src, no package manifest). Neither
  infinite scroll nor the split editor was built. No sign handling in any view. Errors render as readable
  text, not a raw Problem Details body. Semantic markup: real labels, a table for tabular data,
  keyboard-reachable controls.
  Ruling: DEFER the type-ahead gap. The spec says "category as a type-ahead"; the brief's markup is an
    explicit plain <select> and the implementer followed it. A native <select> does give keyboard
    type-ahead (typing letters jumps to matching options), the starter tree is nine categories, and a custom
    combobox with no build step is real risk for little gain at this size. Recorded for phase 7, where a
    larger tree may justify a proper autocomplete. Cost if wrong: filtering-as-you-type arrives a phase late.
  Ruling: FIX the untested antiforgery path. The header-based design (AddAntiforgery with a custom
    HeaderName, plus hx-headers on <body>) is the implementer's own, filling a gap the brief described only
    in prose, and it was verified once with a scratch test that was then DELETED. A misconfigured antiforgery
    setup either blocks every legitimate POST or silently protects nothing, and the Remove button posts by
    header alone because it is not inside a form. One committed test pins it. Cost if wrong: one test.
Task 30: FINDING (deferred, carried) — the page quick-add posts to /transactions?handler=QuickAdd, a Razor
  handler, NOT to /api/v1/transactions/quick-expense. Task 28's IdempotencyFilter is attached to the API
  endpoints only, so the UI path — the one users actually use — has NO server-side duplicate protection;
  its only guard is hx-disabled-elt, which is client-side. I confirmed by grep that no Idempotency-Key is
  sent from any page. Practical risk is low for a local single-user app where HTMX disables the control
  synchronously before the request, but the asymmetry is real: the protected path is the one nobody uses.
  Closing it properly means running the filter over Razor handlers, which is a design change beyond this
  task. CARRIED INTO TASK 33 for the acceptance round-trip, and recorded for the phase 7 UI pass.
Task 30: fix round 1/5 (1 addressed, 0 open — commits 8b76b4e..1aa8a42, test file only). Antiforgery now
  pinned in BOTH directions: a POST to the Void handler without a token asserts BadRequest (observed, not
  assumed — it passed on the first run), and a POST WITH a token obtained the way a browser does (GET the
  page, extract the token from the rendered HTML with a regex, send it as the header) asserts 200 AND
  independently verifies IsVoided == true through the JSON API. That second check matters: the implementer
  found that asserting against the HTML partial would have been worthless, because the partial excludes
  voided rows by default and would look identical whether the void succeeded or not. The "stop and tell me
  if enforcement is missing" clause did not trigger — enforcement is real.
Task 30: complete (commits 5750047..1aa8a42, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  Api.Tests 38/38, Application.Tests 104/104 — 390 total, whole suite green.
Task 30: minor (deferred): the `new` modifier on the Page property suppresses CS0108; renaming the property
  would avoid the collision entirely.
Task 31: review 1 — spec compliant, ZERO findings at any severity. Both screens implement every capability
  the spec names. All tree, rename, reparent and archive behaviour is DELEGATED to the existing handlers —
  nothing reimplemented in a page model. The archive refusal reaches the user as readable, actionable text
  ("'{name}' still has active {categories|accounts} underneath it. Archive those first.") rendered as an
  error paragraph, not a raw body. Sign handling correct: the page reads Balance straight from
  GetAccountBalanceHandler, which already went through DisplayAmountMapper, and the view only formats it.
  Tree rendered as nested ul/li via a recursive partial, accounts as a real table with thead/tbody. All four
  new HTMX controls are descendants of <body>, so they inherit the antiforgery token Task 30 pinned.
  CONTROLLER-VERIFIED sweep of src/Money.Api/Pages/ for posting|debit|credit|double-entry|ledger|
  "expense account" — ZERO hits; and for "delete" — ZERO hits, so archive really is the only removal.
  Ruling: ACCEPT deferring the widening of No_page_uses_accounting_vocabulary to Task 32. It still iterates
    only ["/transactions"], with its own comment naming Task 32 as the place it grows. The new pages are
    hand-verified clean (mine and the implementer's greps agree) and Task 33 runs a full vocabulary review.
    CARRIED INTO TASK 32 — the test must actually be widened there, or the guard never materialises.
  Ruling: ACCEPT relying on the shared antiforgery mechanism rather than adding per-page tests. The token
    lives on <body> in _Layout.cshtml and Task 30 pinned it in both directions against a control outside a
    form; every new control here sits in the same inheritance.
Task 31: complete (commits 177cc7f..c56fa94, review clean, no fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  Api.Tests 41/41, Application.Tests 104/104 — 393 total, whole suite green.
Task 32: review 1 — spec compliant except one Important. The CARRIED OBLIGATION IS DISCHARGED: the
  vocabulary test now sweeps all five pages (/transactions, /categories, /accounts, /settings, /FirstRun)
  and carries a comment telling future authors to widen it again when a page is added. The anchor-day
  rejection surfaces the domain explanation verbatim — "Days above 28 do not exist in every month, which
  would make period boundaries ambiguous" — not a bare "invalid" and not a silent clamp. The wizard is
  idempotent on a second submit (FirstRunCompleted checked before any write) and redirects home when
  revisited after setup. No "Equity" or other implementation detail leaks into rendered output.
  1 Important:
    F1 FirstRun.cshtml had form controls (currency select, account name, account role) inside
       fieldset/legend groups with NO individual labels. A legend names the group, not the control, so a
       screen-reader user tabbing in hears the group.
  Ruling: FIX IT rather than accept the "existing convention" defence, which does not survive inspection —
    Settings.cshtml, written in the SAME task, labels all its controls including its selects, so the new
    code contradicted itself. "Real labels tied to inputs" is a standing requirement I stated, and the
    wizard is the one screen a user cannot skip. Same class of conflict as the enum-storage question: the
    brief's verbatim markup lost to the stated constraint. Cost if wrong: three extra labels.
Task 32: fix round 1/5 (1 addressed, 0 open — commits 4c44b4a..aa255b3, FirstRun.cshtml only). All three
  controls now wrapped in labels, matching the wrapping style Settings.cshtml already used rather than a
  third pattern; fieldset/legend grouping preserved. No asserted string changed and the suite matches the
  pre-fix baseline exactly at 397.
Task 32: complete (commits 28d758c..aa255b3, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  Api.Tests 45/45, Application.Tests 104/104 — 397 total, whole suite green.
Task 32: minor (deferred): the wizard's "Create starter categories:" wording differs from the brief's
  "Create a starter set of categories" because the brief's own test asserted on a string its own markup did
  not contain. Semantics preserved.
Task 33: STRYKER GATE INVESTIGATION (controller, before finishing the task).
  A first run reported 36.36%, below the break threshold of 80, which would have been a 57-point collapse
  from Task 16's 93.81%. It was a bad measurement, not a regression.
  Evidence: two clean sequential runs both report "The final mutation score is 93.26 %" with Killed 318,
  Survived 15 — the SAME kill/survive counts recorded at Task 16. (93.26 vs 93.81 is the 8 no-coverage
  mutants entering the denominator as the tree grew: 318/(333+8) = 93.26.) BalanceCalculator.cs and
  Transaction.cs are both still at 100%.
  The bad run's per-file table showed DomainErrors.cs at 0.0% with 90 survived / 0 killed and Result.cs at
  8.3% with 1 killed / 15 survived — i.e. whole files where no covering test executed at all, which is the
  signature of a broken test run rather than lost assertions.
  Probable cause, not proven: that run was launched in the BACKGROUND immediately after a foreground
  `dotnet build` plus a full `dotnet test`, so Stryker's initial test run and coverage capture most likely
  raced leftover test-host processes and file locks. The run's own header was lost because I piped it
  through `tail -20`, so the mechanism cannot be confirmed from the log. Consistent with the one-off flaky
  Stryker timeout already recorded at Task 9.
  Ruling: the gate HOLDS at 93.26%. Do not touch the threshold, add an exclusion or weaken a test. Operational
    lesson for the phase 3-8 plans: run Stryker sequentially, never concurrently with another dotnet test
    host, and capture its full log rather than a tail.
Task 33: NOTE — the original implementer was cut off by a rate limit mid-task; a fresh agent inherited the
  uncommitted tree and finished. The acceptance pass found TWO real defects in earlier work, which is what
  it exists for.
  Defect A (fixed in d63175d): TransactionMapper.ToPageDto computed the list amount as
    SignedAmountMinor / currency.MinorUnitScale, BYPASSING DisplayAmountMapper — a display conversion
    outside the one place the sign convention may be applied. It looked right for an expense headline, but
    an opening balance has no expense leg, so the headline fell to the largest-magnitude line, which can be
    the Equity leg (stored credit-negative), and the row rendered as -1500. The fix routes through
    DisplayAmountMapper.ToDisplay, has LedgerQueries return the headline leg's AccountKind, and prefers the
    ASSET leg when there is no expense leg so the Amount and Account columns describe the same side.
    IMPORTANT for my own record: my earlier "no sign flip outside the mapper" greps had a blind spot. They
    searched for `* -1`, Math.Abs and unary minus, and never would have caught a raw division by
    MinorUnitScale. I re-checked every remaining MinorUnitScale use in src/: all are INBOUND conversions
    through Money.RoundToMinor feeding LedgerTemplates, plus Money.ToDecimal itself. The one-mapper rule
    holds, but it held with one hole in it until this task.
  Defect B (fixed in ffd6fe7): the FirstRun wizard's "As of" date bound to an uninitialised DateOnly, so the
    first screen a user ever sees rendered 0001-01-01. Ruling: FIX rather than defer to the follow-up chip
    the implementer spawned — this is the last task in the plan, so deferring means shipping it, and phase
    2's acceptance is "a usable manual expense tracker" with first run as the entry point. Fixed via
    TodayResolver on the GET path only (no ambient time; a failed POST still keeps what the user typed).
  Three vocabulary leaks found BY HAND, not by grep, and fixed: DomainErrors.Account.NameUnusable said
    "path segment"; the void-error family said "voided" while the UI's own word is "Removed"; Currency.Unknown
    said "known-currency table". The reviewer verified none broke an assertion (only .Code is pinned in
    tests, and Currency.Unknown still contains the code) and that "removed" matches the UI's existing
    wording. This is exactly what a machine sweep could not have found.
Task 33: review 1 — 1 CRITICAL. The round-trip test's transfer step asserted NOTHING about the transfer:
    the response was discarded with no status check and no balance was re-read afterwards. The only
    downstream assertion was that the Groceries balance was unchanged — vacuous, since Groceries is not a
    party to a bank-to-savings transfer, so it held whether the transfer succeeded, failed or never ran. The
    test would have passed against a broken or no-op transfer endpoint. That is the exact clause the task
    exists to prove (I12, the spec's headline promise).
  Ruling: fix, with a teeth-proof. Proving I12 needs BOTH halves — the money really moved, AND spending did
    not change — and only the second was asserted.
Task 33: fix round 1/5 (1 addressed, 0 open — commits ffd6fe7..cb02290, test file only). Now asserts 201
  Created, bank debited by exactly 300, savings credited by exactly 300 (both sides, so a debit without a
  credit cannot pass), and only then that spending is unchanged. Teeth proven: breaking the transfer amount
  produced "Expected bankAfterTransfer!.Balance to be 1157.65M ... but found 1456.65M".
Task 33: complete (commits 7a61c2b..cb02290, review clean after 1 fix round)
  Verified by controller: build 0 warnings/0 errors; Domain.Tests 241/241, Architecture.Tests 7/7,
  Api.Tests 48/48, Application.Tests 106/106 — 402 total; mutation score 93.26% over two sequential runs.
Task 33: minor (deferred): "Enter saves the quick-add row" is unverified by any test — browser automation
  could not submit on a synthetic Enter, and a plain no-JS control form failed the same way, so the harness
  is the likely limitation rather than the product. The source supports it (one type="submit" button, no
  keydown interceptor). Wants a human keyboard spot-check in a real browser.

=== ALL 33 TASKS COMPLETE ===

=== FINAL WHOLE-BRANCH REVIEW (opus, 84 commits, 6d4e4ea..7ced5e9) ===
Verdict: FIX FIRST. Three Critical blockers, all cross-task disconnects invisible to any single-task diff
and to all 402 tests. Reviewer confirmed every invariant I1-I5, I11, I12 holds, and confirmed by independent
sweep that DisplayAmountMapper really is the only sign flip in the solution.
  C1 BASE CURRENCY HONOURED ONLY AT FIRST RUN. Accounts.cshtml.cs:28 passes literal "EUR";
     CreateAccountHandler.cs:30 falls back to Currency.Eur.Code; CreateCategoryHandler.cs:37 gives every root
     category Currency.Eur. CompleteFirstRunSetupHandler uses request.BaseCurrencyCode and the wizard offers
     EUR/HUF/USD/GBP. So a HUF user gets a HUF ledger and every account or root category created afterwards
     is EUR — permanently dead, since recording against it fails CurrencyMismatchWithAccount and no path
     changes an account's currency. Invisible to the suite because every test passes "EUR" explicitly.
  C2 PatchAccountHandler LOSES DESCENDANTS on rename+move in one PATCH. Line 41 re-reads
     DescendantsOfAsync(account.ChildPathPrefix) AFTER the rename branch mutated Path in memory; the SQL
     StartsWith runs against unsaved data still holding old paths, matches zero rows, so AccountTree.Move
     rewrites only the node and children keep stale paths. Materialised paths are what SubtreeBalanceAsync,
     DescendantsOfAsync and the category tree all rest on. The Move branch has NO test.
  C3 BACKUP RETENTION SETTING DOES NOTHING. DependencyInjection.cs:52 hardcodes RetentionCount: 10; the
     Settings screen renders, clamps, persists and re-displays the value, and SqliteBackupService always uses
     the hardcoded 10. A data-retention control that appears to work and does not.
  I1 CreateAccountHandler.cs:66 dates an opening balance with DateOnly.FromDateTime(now.UtcDateTime) — a
     second way to compute "today" that bypasses PeriodResolver, which CLAUDE.md forbids. Every other date
     default routes through TodayResolver. Not caught by the architecture test because `now` comes from IClock.
  I2 Four raw `x * MinorUnitScale` conversions outside DisplayAmountMapper (CreateAccountHandler:62,
     CompleteFirstRunSetupHandler:62, QuickExpenseHandler:35, TransferHandler:37). No live sign bug — all sit
     on Asset/Expense legs where ToStored is a no-op — but CreateAccountHandler:62 is safe only by accident,
     converting for an account of arbitrary Kind and rescued by a guard in LedgerTemplates.
  I3 _TransactionRows.cshtml:34 and _AccountRows.cshtml:19 hardcode ToString("N2"), so JPY (exponent 0)
     renders two invented decimals. Money.ToString already formats by exponent.
  Deferred-minor triage: NONE of the ledger's 57 deferred minors must be fixed before merge. Two promoted to
    "next, not later" because the blockers sit on them: the Task 20 currency-filter comment (now load-bearing
    documentation, since C1 shows EUR accounts can leak into a non-EUR ledger) and the Task 11 duplicated
    root-vs-child path ternary (consolidate while fixing C2).
  Evidence caveat recorded: LedgerGen is all-EUR, so the per-currency clause in SubtreeBalance and
    TotalSpending is exercised by no property — only BalanceOf has a cross-currency example test. And
    Every_generated_transaction_balances_to_zero / _has_at_least_two_entries cannot return false: Build2 ends
    in .Value, which throws before the assertion. They are load-bearing through the crash, not the predicate.
