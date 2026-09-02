# Money Tracker — Implementation Plan, Phases 0–2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a working, offline, single-user money tracker: a double-entry ledger with a category tree, persisted to SQLite, exposed through a versioned REST API and a Razor + HTMX web UI, so that the user can add an expense and see balances and history change — delivered test-first with the ledger invariants proven by property tests.

**Architecture:** Five source projects with dependencies pointing inward only (`Api → Infrastructure → Application → Domain`), enforced by architecture tests. `Money.Domain` is pure: value objects (`Money`, `Currency`, `PeriodKey`), the account tree, the transaction aggregate that refuses to exist unless its postings balance, and pure calculators. `Money.Application` holds use cases behind ports. `Money.Infrastructure` owns EF Core, SQLite and file I/O. `Money.Api` is the composition root plus Minimal API endpoints and server-rendered Razor Pages driven by HTMX.

**Tech Stack:** C# / .NET 9, ASP.NET Core Minimal API, Razor Pages + HTMX (vendored, no Node), EF Core 9 + SQLite (WAL), xUnit, FluentAssertions, CsCheck (property tests), NetArchTest, Stryker.NET, GitHub Actions.

**Spec:** [docs/superpowers/specs/2026-09-01-money-tracker-design.md](../specs/2026-09-01-money-tracker-design.md)

**Scope of this plan:** Spec section 13, phases **0 (Foundation)**, **1 (Ledger core)** and **2 (Persistence + shell)**. Phases 3–8 (recurring, budgets, pockets, investments, dashboard, packaging) get their own plans, written after this one is executed so they can be written against real code. Nothing in this plan may implement them — but the seams they need (`SourceKind`, the idempotency unique index, `PeriodResolver`, `Role=SavingsPocket`/`Investment`) are built here, and each is called out where it appears.

### Two things the spec puts in phase 2 that this plan deliberately defers

Recorded here so they read as decisions, not oversights. Both are carried into the phase 8 plan's scope.

1. **Database restore** (spec section 8: *"a restore path exists in the admin screen: pick a backup, the app validates it, swaps the file and restarts"*). Backup, export and the integrity check all ship here (Tasks 25 and 29). Restore does not, because *"and restarts"* has no meaning until there is a host process to restart — in phase 2 the app is `dotnet run`. Swapping the file underneath a live Kestrel with an open WAL connection is the one operation most likely to destroy the ledger, and it deserves to be built once, against the real WebView2 host, in phase 8. The user is not stranded meanwhile: backups are real SQLite files, and restoring one by hand is a file copy while the app is closed. `docs/running-locally.md` (Task 33) says so.

2. **Infinite scroll and the split editor** on the transactions screen (spec section 10). The *capability* ships: `GET /transactions` pages by cursor (Task 20) and `POST /transactions` accepts any number of lines, both covered by tests (Tasks 20, 28). What ships in the UI is a 50-row list and a single-category quick-add. The richer editing surface belongs with the phase 7 UI pass, when there is a dashboard to design it alongside. Phase 2's acceptance criterion is *"a usable manual expense tracker"*, and it is met without them.

---

## Global Constraints

Every task's requirements implicitly include this section. Values are copied verbatim from the spec.

- **Runtime:** .NET 9. `TargetFramework=net9.0` for every project.
- **Warnings are errors.** `TreatWarningsAsErrors=true`, `Nullable=enable` solution-wide.
- **Money is `long` minor units + an ISO-4217 code.** Rates and percentages are `decimal`. (D8)
- **`double` and `float` must not appear anywhere in `Money.Domain`.** Enforced by an architecture test. No implicit conversion to or from floating-point types anywhere in the solution's money paths.
- **SQLite column types:** amounts are `INTEGER`, rates and percentages are `NUMERIC`. **`REAL` is never used.** (D9)
- **Sign convention (fixed, never re-litigated):** *positive = debit, negative = credit.* Asset and Expense accounts increase with a positive amount. Income, Liability and Equity accounts increase with a negative amount. Income therefore carries a negative internal balance and is negated **in exactly one mapper** for display (Task 17).
- **No ambient time.** `IClock` is injected everywhere. `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` and `DateOnly.FromDateTime(DateTime.Now)` are forbidden outside the composition root (`Money.Infrastructure/Time/SystemClock.cs`, `Money.Api/Program.cs`, `Money.Desktop/`). Enforced by an architecture test. No test reads the system clock; `FakeClock` everywhere.
- **Nothing outside `PeriodResolver` computes period boundaries.**
- **Deletes do not exist.** Transactions are voided (`IsVoided` + `VoidReason`). Reference data is archived (`IsArchived`). (D11)
- **Database location:** `%APPDATA%/MoneyApp/money.db`, overridable via the `MONEYAPP_DATA_DIR` environment variable. **Never** beside the executable. (D10)
- **SQLite pragmas:** `journal_mode=WAL`, `foreign_keys=ON`, `busy_timeout=5000`.
- **Every table has `CreatedAtUtc` and `UpdatedAtUtc`.**
- **Errors:** `Result<T>` for expected failures. Exceptions only for programmer error. The API maps domain errors to RFC 9457 Problem Details.
- **API:** REST under `/api/v1`. OpenAPI document served at `/openapi/v1.json`.
- **UI vocabulary:** the words *posting*, *debit*, *credit*, *ledger* and *double-entry* must never appear in a view rendered to the user. Categories are called **Categories**, never "expense accounts".
- **Test doubles:** an in-memory EF *provider* is never used. Application and API tests run against real SQLite (`DataSource=:memory:` with the connection held open) so real check constraints and real unique indexes execute.
- **Commit style:** Conventional Commits (`feat:`, `test:`, `chore:`, `fix:`, `docs:`). Commit at the end of every task, and at each `Commit` step inside a task.

### Deviation from the spec's project list, recorded deliberately

The spec's section 7 lists four test projects. This plan adds a fifth, `tests/Money.TestSupport/` — a non-test class library holding `FakeClock`, the SQLite fixture and the ledger builders. Without it, `FakeClock` would be copy-pasted into four projects, or test projects would reference each other. It contains no tests and is referenced only by test projects.

---

## File Structure

Files created by this plan, and what each is responsible for. Later phases add to these directories; nothing here should grow beyond its stated responsibility.

```
Money.sln
Directory.Build.props                  shared MSBuild settings (net9.0, nullable, warnings-as-errors)
Directory.Packages.props               central package version management
stryker-config.json                    mutation-testing config, targets Money.Domain
.github/workflows/ci.yml               restore, build, test, architecture tests, Stryker
.editorconfig                          formatting + analyzer severity

src/Money.Domain/
  Primitives/Result.cs                 Result, Result<T>
  Primitives/DomainError.cs            DomainError record
  Primitives/DomainErrors.cs           the error catalogue — one factory per failure code
  Money/Currency.cs                    ISO-4217 code + minor-unit exponent, known-currency table
  Money/Money.cs                       long minor units + Currency, checked arithmetic, rounding
  Money/CurrencyMismatchException.cs   programmer-error exception
  Time/IClock.cs                       the only source of time
  Periods/PeriodType.cs                Weekly | Monthly | Quarterly | Yearly
  Periods/PeriodAnchor.cs              CalendarMonth | DayOfMonth(1..28)
  Periods/PeriodDefinition.cs          anchor + IANA time zone + first day of week
  Periods/PeriodKey.cs                 (Type, Year, Index) + string form "2026-M09"
  Periods/DateRange.cs                 [Start, EndExclusive)
  Periods/PeriodResolver.cs            the ONLY component computing period boundaries
  Accounts/AccountKind.cs              Asset | Liability | Income | Expense | Equity
  Accounts/AccountRole.cs              Bank | Cash | SavingsPocket | Investment | Category | OpeningBalance | Adjustment
  Accounts/Account.cs                  the account/category tree node, materialised Path
  Accounts/AccountTree.cs              pure subtree operations (reparent path recompute)
  Ledger/TransactionSourceKind.cs      Manual | Recurring | Accrual | Import
  Ledger/PostingDraft.cs               input value for building a transaction
  Ledger/Posting.cs                    persisted posting
  Ledger/Transaction.cs                the aggregate that cannot exist unbalanced
  Ledger/BalanceCalculator.cs          pure reference implementation of every balance query
  Ledger/LedgerTemplates.cs            quick-expense / transfer / opening-balance builders

src/Money.Application/
  Abstractions/                        ports: repositories, queries, unit of work, services
  Contracts/                           request + response DTOs, one file per feature
  Presentation/DisplayAmountMapper.cs  THE single place the sign convention is negated
  Accounts/                            CreateAccount, PatchAccount, ArchiveAccount, ListAccounts, GetAccountBalance
  Categories/                          CreateCategory, GetCategoryTree
  Transactions/                        Create, Get, Replace, Void, List, QuickExpense, Transfer
  Settings/                            GetSettings, UpdateSettings
  FirstRun/CompleteFirstRunSetup.cs    seeds settings, first account, starter category tree
  Admin/                               Backup, Export, IntegrityCheck use cases

src/Money.Infrastructure/
  Persistence/MoneyDbContext.cs
  Persistence/Configurations/          one IEntityTypeConfiguration per aggregate
  Persistence/Migrations/              EF Core migrations
  Persistence/Repositories/            EF implementations of the repository ports
  Persistence/LedgerQueries.cs         raw SQL balance + list queries
  Persistence/IntegrityChecker.cs      the zero-sum sweep that SQLite cannot express as a check
  Persistence/DataDirectory.cs         %APPDATA%/MoneyApp or MONEYAPP_DATA_DIR
  Persistence/DatabaseInitializer.cs   pre-migration backup, migrate, pragmas
  Persistence/SettingsEntity.cs        the single settings row
  Backup/SqliteBackupService.cs        SQLite Online Backup API + retention
  Export/JsonExportService.cs
  Export/CsvExportService.cs
  Time/SystemClock.cs                  composition root: the only DateTimeOffset.UtcNow
  Identity/LocalCurrentUser.cs

src/Money.Api/
  Program.cs                           composition root, hosting mode switch
  Endpoints/AccountEndpoints.cs
  Endpoints/CategoryEndpoints.cs
  Endpoints/TransactionEndpoints.cs
  Endpoints/SettingsEndpoints.cs
  Endpoints/AdminEndpoints.cs
  Infrastructure/DomainErrorResults.cs DomainError -> RFC 9457 Problem Details
  Infrastructure/IdempotencyFilter.cs  Idempotency-Key handling for POST /transactions
  Pages/                               Razor Pages + HTMX partials
  wwwroot/                             app.css, vendored htmx.min.js, PWA manifest

src/Money.Desktop/
  (scaffolded empty in Task 1; implemented in phase 8)

tests/Money.TestSupport/               FakeClock, SqliteFixture, ledger builders (not a test project)
tests/Money.Domain.Tests/              unit + CsCheck property tests, no I/O
tests/Money.Application.Tests/         use cases against real in-memory SQLite
tests/Money.Api.Tests/                 WebApplicationFactory integration tests
tests/Money.Architecture.Tests/        NetArchTest + source-scanning rules
```

---

# Phase 0 — Foundation

**Deliverable:** solution, nine projects, green CI pipeline, `Money`, `Currency`, `PeriodKey`, `PeriodResolver`, `IClock`.
**Acceptance:** green pipeline; architecture tests pass; the period tiling property test (I9) passes.

---

### Task 1: Solution scaffolding and a green pipeline

**Files:**
- Create: `Money.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`
- Create: `src/Money.Domain/Money.Domain.csproj`, `src/Money.Application/Money.Application.csproj`, `src/Money.Infrastructure/Money.Infrastructure.csproj`, `src/Money.Api/Money.Api.csproj`, `src/Money.Desktop/Money.Desktop.csproj`
- Create: `tests/Money.TestSupport/Money.TestSupport.csproj`, `tests/Money.Domain.Tests/Money.Domain.Tests.csproj`, `tests/Money.Application.Tests/Money.Application.Tests.csproj`, `tests/Money.Api.Tests/Money.Api.Tests.csproj`, `tests/Money.Architecture.Tests/Money.Architecture.Tests.csproj`
- Create: `.github/workflows/ci.yml`
- Test: `tests/Money.Domain.Tests/SolutionSmokeTests.cs`

**Interfaces:**
- Produces: a buildable solution where `dotnet test` runs and passes; every later task assumes these project names and reference directions.

- [x] **Step 1: Create the solution and projects**

Run from the repository root:

```bash
dotnet new sln -n Money
dotnet new classlib -o src/Money.Domain -f net9.0
dotnet new classlib -o src/Money.Application -f net9.0
dotnet new classlib -o src/Money.Infrastructure -f net9.0
dotnet new web -o src/Money.Api -f net9.0
dotnet new console -o src/Money.Desktop -f net9.0
dotnet new classlib -o tests/Money.TestSupport -f net9.0
dotnet new xunit -o tests/Money.Domain.Tests -f net9.0
dotnet new xunit -o tests/Money.Application.Tests -f net9.0
dotnet new xunit -o tests/Money.Api.Tests -f net9.0
dotnet new xunit -o tests/Money.Architecture.Tests -f net9.0
```

Then add every project to the solution:

```bash
find src tests -name '*.csproj' -exec dotnet sln add {} +
```

Delete the generated `Class1.cs` from each class library.

- [x] **Step 2: Wire the reference graph — inward only**

```bash
dotnet add src/Money.Application reference src/Money.Domain
dotnet add src/Money.Infrastructure reference src/Money.Application
dotnet add src/Money.Api reference src/Money.Infrastructure
dotnet add src/Money.Desktop reference src/Money.Api
dotnet add tests/Money.TestSupport reference src/Money.Infrastructure
dotnet add tests/Money.Domain.Tests reference src/Money.Domain tests/Money.TestSupport
dotnet add tests/Money.Application.Tests reference src/Money.Application src/Money.Infrastructure tests/Money.TestSupport
dotnet add tests/Money.Api.Tests reference src/Money.Api tests/Money.TestSupport
dotnet add tests/Money.Architecture.Tests reference src/Money.Api src/Money.Desktop
```

`Money.Api` never gets a direct reference to `Money.Domain` — it reaches the domain transitively. This keeps the dependency arrow honest and the architecture test simple.

- [x] **Step 3: Write `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <!-- MUST stay false: TimeZoneInfo.FindSystemTimeZoneById needs ICU for IANA ids -->
    <InvariantGlobalization>false</InvariantGlobalization>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

- [x] **Step 4: Write `Directory.Packages.props`**

`FluentAssertions` is pinned to `6.12.2` deliberately: version 8 moved to a paid commercial licence. 6.12.2 is Apache-2.0. Do not let a tool "helpfully" upgrade it.

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="9.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="FluentAssertions" Version="6.12.2" />
    <PackageVersion Include="CsCheck" Version="4.2.0" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
    <PackageVersion Include="coverlet.collector" Version="6.0.2" />
  </ItemGroup>
</Project>
```

Because central package management is on, each `.csproj` uses `<PackageReference Include="X" />` with **no** `Version` attribute. Remove the versions the `dotnet new` templates wrote into the test projects.

Add to all four test projects: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`, `coverlet.collector`.
Add `CsCheck` to `Money.Domain.Tests`. Add `NetArchTest.Rules` to `Money.Architecture.Tests`.

Add a global using for FluentAssertions to each test project so the test code in this plan compiles as written — create `GlobalUsings.cs` in each:

```csharp
global using FluentAssertions;
global using Xunit;
```

- [x] **Step 5: Write `.editorconfig`**

```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
end_of_line = crlf
insert_final_newline = true
csharp_style_namespace_declarations = file_scoped:error
dotnet_diagnostic.CA1062.severity = none
dotnet_diagnostic.CA2007.severity = none

[*.{csproj,props,targets,json,yml}]
indent_size = 2
```

- [x] **Step 6: Write the smoke test**

`tests/Money.Domain.Tests/SolutionSmokeTests.cs`:

```csharp
namespace Money.Domain.Tests;

public sealed class SolutionSmokeTests
{
    [Fact]
    public void The_test_host_runs()
    {
        (2 + 2).Should().Be(4);
    }
}
```

Deliberately independent of any domain type, so Task 1 ends green.

- [x] **Step 7: Verify the whole solution builds and tests pass**

Run: `dotnet build -warnaserror` then `dotnet test`
Expected: build succeeds with zero warnings; all four test projects report passed.

- [x] **Step 8: Write the CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore -warnaserror -c Release
      - name: Test
        run: dotnet test --no-build -c Release --collect:"XPlat Code Coverage" --logger "trx;LogFileName=test-results.trx"
      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/TestResults/**'
```

`Money.Desktop` builds fine on `ubuntu-latest` until phase 8 adds WebView2. Phase 8's plan adds a `win-x64` job and excludes the desktop project from the Linux job.

- [x] **Step 9: Commit**

```bash
git add -A && git commit -m "chore: scaffold solution, nine projects, central package management and CI"
```

---

### Task 2: Architecture tests

**Files:**
- Create: `tests/Money.Architecture.Tests/RepositoryRoot.cs`
- Test: `tests/Money.Architecture.Tests/DependencyRuleTests.cs`, `DomainPurityTests.cs`, `AmbientTimeTests.cs`

**Interfaces:**
- Consumes: the project reference graph from Task 1.
- Produces: `internal static class RepositoryRoot` with `string Find()` returning the absolute path of the directory containing `Money.sln`.

These tests are written first and stay green forever. They are the mechanism that stops the architecture eroding during phases 3–8, so they are worth their weight now, while there is nothing to fix.

- [x] **Step 1: Write the repository-root helper**

```csharp
namespace Money.Architecture.Tests;

internal static class RepositoryRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Money.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Money.sln not found above " + AppContext.BaseDirectory);
    }
}
```

- [x] **Step 2: Add assembly markers so the tests have something to point at**

`Money.Application` and `Money.Domain` are still empty. Create one marker type in each so these tests compile now and keep compiling later:

`src/Money.Domain/DomainAssemblyMarker.cs`:

```csharp
namespace Money.Domain;

/// <summary>Anchor type for assembly-scanning tests. Holds no behaviour.</summary>
public sealed class DomainAssemblyMarker;
```

`src/Money.Application/ApplicationAssemblyMarker.cs`:

```csharp
namespace Money.Application;

/// <summary>Anchor type for assembly-scanning tests. Holds no behaviour.</summary>
public sealed class ApplicationAssemblyMarker;
```

- [x] **Step 3: Write the failing dependency-rule tests**

```csharp
using NetArchTest.Rules;

namespace Money.Architecture.Tests;

public sealed class DependencyRuleTests
{
    private static readonly System.Reflection.Assembly Domain =
        typeof(Money.Domain.DomainAssemblyMarker).Assembly;

    private static readonly System.Reflection.Assembly Application =
        typeof(Money.Application.ApplicationAssemblyMarker).Assembly;

    [Fact]
    public void Domain_references_nothing_but_the_base_class_library()
    {
        var offenders = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                        && name != "netstandard"
                        && name != "mscorlib")
            .ToArray();

        offenders.Should().BeEmpty(
            "Money.Domain must have zero project and third-party dependencies");
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_api()
    {
        var offenders = Application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name is "Money.Infrastructure" or "Money.Api")
            .ToArray();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Domain_types_do_not_depend_on_application_or_higher()
    {
        Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Money.Application", "Money.Infrastructure", "Money.Api")
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
```

- [x] **Step 4: Write the failing domain-purity tests**

```csharp
using System.Reflection;

namespace Money.Architecture.Tests;

public sealed class DomainPurityTests
{
    private static readonly Assembly Domain = typeof(Money.Domain.DomainAssemblyMarker).Assembly;

    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Fact]
    public void No_floating_point_types_appear_in_the_domain()
    {
        var offenders = new List<string>();

        foreach (var type in Domain.GetTypes())
        {
            foreach (var field in type.GetFields(All))
            {
                if (IsFloating(field.FieldType)) offenders.Add($"{type.FullName}.{field.Name}");
            }

            foreach (var property in type.GetProperties(All))
            {
                if (IsFloating(property.PropertyType)) offenders.Add($"{type.FullName}.{property.Name}");
            }

            foreach (var method in type.GetMethods(All | BindingFlags.DeclaredOnly))
            {
                if (IsFloating(method.ReturnType)) offenders.Add($"{type.FullName}.{method.Name} (return)");
                foreach (var p in method.GetParameters())
                {
                    if (IsFloating(p.ParameterType)) offenders.Add($"{type.FullName}.{method.Name}({p.Name})");
                }
            }
        }

        offenders.Should().BeEmpty("floating point must never touch money (spec D8)");

        static bool IsFloating(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t.IsByRef || t.IsArray) t = t.GetElementType() ?? t;
            return t == typeof(double) || t == typeof(float) || t == typeof(Half);
        }
    }

    [Fact]
    public void No_public_domain_type_exposes_a_mutable_collection()
    {
        var mutable = new[]
        {
            typeof(List<>), typeof(ICollection<>), typeof(IList<>),
            typeof(Dictionary<,>), typeof(IDictionary<,>), typeof(HashSet<>), typeof(ISet<>)
        };

        var offenders = Domain.GetTypes()
            .Where(t => t.IsPublic)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                              .Select(p => (Type: t, Property: p)))
            .Where(x =>
            {
                var pt = x.Property.PropertyType;
                if (pt.IsArray) return true;
                if (!pt.IsGenericType) return false;
                return mutable.Contains(pt.GetGenericTypeDefinition());
            })
            .Select(x => $"{x.Type.FullName}.{x.Property.Name}")
            .ToArray();

        offenders.Should().BeEmpty();
    }
}
```

- [x] **Step 5: Write the failing ambient-time test**

This one scans source, because forbidding a *call* is an IL-level question that reflection cannot answer cleanly. A source scan is exact, fast, and reads well in a failure message.

```csharp
using System.Text.RegularExpressions;

namespace Money.Architecture.Tests;

public sealed class AmbientTimeTests
{
    private static readonly string[] AllowedPathFragments =
    [
        Path.Combine("Money.Infrastructure", "Time", "SystemClock.cs"),
        Path.Combine("Money.Api", "Program.cs"),
        Path.Combine("Money.Desktop", "")
    ];

    private static readonly Regex Forbidden = new(
        @"DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.Now|DateTimeOffset\.UtcNow|DateOnly\.FromDateTime\s*\(\s*DateTime\.",
        RegexOptions.Compiled);

    [Fact]
    public void Ambient_time_is_only_read_in_the_composition_root()
    {
        var src = Path.Combine(RepositoryRoot.Find(), "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !AllowedPathFragments.Any(a => f.Contains(a, StringComparison.Ordinal)))
            .Where(f => Forbidden.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f))
            .ToArray();

        offenders.Should().BeEmpty("time must be injected via IClock (spec section 7)");
    }
}
```

- [x] **Step 6: Run the architecture tests**

Run: `dotnet test tests/Money.Architecture.Tests`
Expected: PASS — all five tests. The tree is empty, so every rule is trivially satisfied. That is the correct starting state: the rules exist before there is anything to break them.

- [x] **Step 7: Prove the ambient-time rule actually catches something**

Temporarily add `public static DateTime Bad => DateTime.UtcNow;` to `src/Money.Application/ApplicationAssemblyMarker.cs`, re-run the test, confirm it FAILS naming that file, then remove it.

A rule nobody has seen fail is a rule nobody knows is wired up.

- [x] **Step 8: Commit**

```bash
git add src tests/Money.Architecture.Tests
git commit -m "test: add architecture tests for the dependency rule, domain purity and ambient time"
```

---

### Task 3: `Result`, `DomainError` and the error catalogue

**Files:**
- Create: `src/Money.Domain/Primitives/DomainError.cs`, `Result.cs`, `DomainErrors.cs`
- Test: `tests/Money.Domain.Tests/Primitives/ResultTests.cs`

**Interfaces:**
- Produces:
  - `sealed record DomainError(string Code, string Message)`
  - `readonly struct Result` — `IsSuccess`, `IsFailure`, `Error`, `Result.Ok()`, `Result.Fail(DomainError)`, `Result.Ok<T>(T)`, `Result.Fail<T>(DomainError)`
  - `readonly struct Result<T>` — `IsSuccess`, `IsFailure`, `Value`, `Error`, `Result<T>.Ok(T)`, `Result<T>.Fail(DomainError)`, `Map`, `Match`, implicit conversion from `DomainError`
  - `static class DomainErrors` — every failure code used in this plan. Task 25 maps `DomainError.Code` onto Problem Details `type` URIs, so codes are a public contract. **Never invent a code inline; add it here.**

- [x] **Step 1: Write the failing tests**

```csharp
using Money.Domain.Primitives;

namespace Money.Domain.Tests.Primitives;

public sealed class ResultTests
{
    private static readonly DomainError SomeError = new("test.failed", "Something went wrong.");

    [Fact]
    public void A_successful_result_carries_its_value()
    {
        var result = Result<int>.Ok(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void A_failed_result_carries_its_error()
    {
        var result = Result<int>.Fail(SomeError);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_is_a_programmer_error()
    {
        var result = Result<int>.Fail(SomeError);

        var act = () => _ = result.Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*test.failed*");
    }

    [Fact]
    public void A_domain_error_converts_implicitly_to_a_failed_result()
    {
        Result<string> result = SomeError;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void A_default_constructed_result_is_a_failure_rather_than_a_silent_success()
    {
        default(Result<int>).IsSuccess.Should().BeFalse();
        default(Result).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Map_transforms_a_success_and_passes_a_failure_through()
    {
        Result<int>.Ok(21).Map(x => x * 2).Value.Should().Be(42);
        Result<int>.Fail(SomeError).Map(x => x * 2).Error.Should().Be(SomeError);
    }

    [Fact]
    public void Match_picks_the_branch_matching_the_outcome()
    {
        Result<int>.Ok(1).Match(v => $"ok:{v}", e => $"err:{e.Code}").Should().Be("ok:1");
        Result<int>.Fail(SomeError).Match(v => $"ok:{v}", e => $"err:{e.Code}").Should().Be("err:test.failed");
    }
}
```

The `default(Result<T>)` test matters: `Result` is a struct, so the default value is reachable (an unassigned field, `new Result<int>[10]`). It must never look like success.

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter ResultTests`
Expected: FAIL to compile — `Money.Domain.Primitives` does not exist.

- [x] **Step 3: Implement `DomainError`**

```csharp
namespace Money.Domain.Primitives;

/// <summary>An expected failure. Programmer errors throw; these are returned.</summary>
public sealed record DomainError(string Code, string Message);
```

- [x] **Step 4: Implement `Result` and `Result<T>`**

```csharp
namespace Money.Domain.Primitives;

public readonly struct Result
{
    private readonly bool _isSuccess;

    private Result(bool isSuccess, DomainError? error)
    {
        _isSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess => _isSuccess;
    public bool IsFailure => !_isSuccess;
    public DomainError? Error { get; }

    public static Result Ok() => new(true, null);

    public static Result Fail(DomainError error) => new(false, error);

    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);

    public static Result<T> Fail<T>(DomainError error) => Result<T>.Fail(error);

    public static implicit operator Result(DomainError error) => Fail(error);
}

public readonly struct Result<T>
{
    private readonly bool _isSuccess;
    private readonly T? _value;

    private Result(bool isSuccess, T? value, DomainError? error)
    {
        _isSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess => _isSuccess;
    public bool IsFailure => !_isSuccess;
    public DomainError? Error { get; }

    public T Value => _isSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Result is a failure ({Error?.Code}: {Error?.Message}); its value cannot be read.");

    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(DomainError error) => new(false, default, error);

    public static implicit operator Result<T>(DomainError error) => Fail(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        _isSuccess ? Result<TOut>.Ok(map(_value!)) : Result<TOut>.Fail(Error!);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<DomainError, TOut> onFailure) =>
        _isSuccess ? onSuccess(_value!) : onFailure(Error!);
}
```

- [x] **Step 5: Implement the error catalogue**

```csharp
namespace Money.Domain.Primitives;

public static class DomainErrors
{
    public static class Currency
    {
        public static DomainError InvalidCode(string code) =>
            new("currency.invalid_code", $"'{code}' is not a three-letter ISO-4217 currency code.");

        public static DomainError Unknown(string code) =>
            new("currency.unknown", $"Currency '{code}' is not in the known-currency table.");

        public static DomainError InvalidMinorUnitExponent(int exponent) =>
            new("currency.invalid_minor_unit_exponent",
                $"Minor-unit exponent {exponent} is outside the supported range 0-4.");
    }

    public static class Period
    {
        public static DomainError AnchorDayOutOfRange(int day) =>
            new("period.anchor_day_out_of_range",
                $"The period anchor day must be between 1 and 28, but was {day}. " +
                "Days above 28 do not exist in every month, which would make period boundaries ambiguous.");

        public static DomainError UnknownTimeZone(string id) =>
            new("period.unknown_time_zone", $"'{id}' is not a time zone this machine recognises.");

        public static DomainError IndexOutOfRange(string type, int index) =>
            new("period.index_out_of_range", $"{index} is not a valid index for a {type} period.");

        public static DomainError UnparsableKey(string text) =>
            new("period.unparsable_key", $"'{text}' is not a valid period key such as '2026-M09'.");

        public static DomainError EndBeforeStart(DateOnly start, DateOnly endExclusive) =>
            new("period.end_before_start",
                $"The end date {endExclusive:O} must be after the start date {start:O}.");
    }

    public static class Account
    {
        public static DomainError NameRequired() =>
            new("account.name_required", "A name is required.");

        public static DomainError NameTooLong(int max) =>
            new("account.name_too_long", $"The name must be {max} characters or fewer.");

        public static DomainError KindRoleMismatch(string kind, string role) =>
            new("account.kind_role_mismatch", $"A '{role}' account cannot have kind '{kind}'.");

        public static DomainError ParentKindMismatch(string childKind, string parentKind) =>
            new("account.parent_kind_mismatch",
                $"A '{childKind}' account cannot sit under a '{parentKind}' parent.");

        public static DomainError ParentArchived(string parentName) =>
            new("account.parent_archived", $"'{parentName}' is archived and cannot take new children.");

        public static DomainError CurrencyMismatchWithParent() =>
            new("account.currency_mismatch_with_parent",
                "A child account must use the same currency as its parent.");

        public static DomainError DuplicateSiblingName(string name) =>
            new("account.duplicate_sibling_name", $"'{name}' already exists at this level.");

        public static DomainError CannotReparentUnderOwnDescendant() =>
            new("account.cannot_reparent_under_own_descendant",
                "An account cannot be moved underneath one of its own descendants.");

        public static DomainError AlreadyArchived(string name) =>
            new("account.already_archived", $"'{name}' is already archived.");

        public static DomainError NotFound(Guid id) =>
            new("account.not_found", $"Account {id} does not exist.");

        public static DomainError NotACategory(string name) =>
            new("account.not_a_category", $"'{name}' is not a category.");

        public static DomainError NameUnusable(string name) =>
            new("account.name_unusable",
                $"'{name}' contains no letters or digits, so it cannot form a path segment.");
    }

    public static class Transaction
    {
        public static DomainError TooFewPostings(int count) =>
            new("transaction.too_few_entries",
                $"A transaction needs at least two entries, but had {count}.");

        public static DomainError DoesNotBalance(string currencyCode, long residualMinor) =>
            new("transaction.does_not_balance",
                $"The {currencyCode} entries do not balance; they are off by {residualMinor} minor units.");

        public static DomainError ZeroAmount() =>
            new("transaction.zero_amount", "An entry cannot be for zero.");

        public static DomainError AccountArchived(string accountName) =>
            new("transaction.account_archived",
                $"'{accountName}' is archived and cannot be used in new entries.");

        public static DomainError AccountUnknown(Guid accountId) =>
            new("transaction.account_unknown", $"Account {accountId} does not exist.");

        public static DomainError CurrencyMismatchWithAccount(string accountName) =>
            new("transaction.currency_mismatch_with_account",
                $"The amount's currency does not match the currency of '{accountName}'.");

        public static DomainError DescriptionRequired() =>
            new("transaction.description_required", "A description is required.");

        public static DomainError DuplicateAccount(string accountName) =>
            new("transaction.duplicate_account",
                $"'{accountName}' appears more than once; combine the entries instead.");

        public static DomainError AlreadyVoided() =>
            new("transaction.already_voided", "This transaction has already been voided.");

        public static DomainError CannotEditVoided() =>
            new("transaction.cannot_edit_voided", "A voided transaction cannot be edited.");

        public static DomainError VoidReasonRequired() =>
            new("transaction.void_reason_required", "A reason is required when voiding.");

        public static DomainError NotFound(Guid id) =>
            new("transaction.not_found", $"Transaction {id} does not exist.");

        public static DomainError DateInFuture(DateOnly occurredOn, DateOnly today) =>
            new("transaction.date_too_far_in_future",
                $"{occurredOn:O} is more than a year after {today:O}; check the date.");
    }

    public static class Settings
    {
        public static DomainError AlreadyInitialised() =>
            new("settings.already_initialised", "First-run setup has already been completed.");

        public static DomainError NotInitialised() =>
            new("settings.not_initialised", "First-run setup has not been completed yet.");
    }

    public static class Admin
    {
        public static DomainError BackupFailed(string reason) =>
            new("admin.backup_failed", $"The backup could not be created: {reason}");

        public static DomainError UnsupportedExportFormat(string format) =>
            new("admin.unsupported_export_format", $"'{format}' is not a supported export format.");
    }
}
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter ResultTests`
Expected: PASS, 7 tests.

- [x] **Step 7: Commit**

```bash
git add src/Money.Domain tests/Money.Domain.Tests
git commit -m "feat: add Result, DomainError and the domain error catalogue"
```

---

### Task 4: `Currency`

**Files:**
- Create: `src/Money.Domain/Money/Currency.cs`
- Test: `tests/Money.Domain.Tests/Money/CurrencyTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `DomainErrors.Currency`.
- Produces: `sealed record Currency` with `string Code`, `int MinorUnitExponent`, `decimal MinorUnitScale`, `static Result<Currency> FromCode(string)`, `static Result<Currency> Create(string code, int minorUnitExponent)`, `static IReadOnlyCollection<Currency> Known`, and static instances `Currency.Eur`, `Currency.Huf`, `Currency.Usd`.

`FromCode` rejects unknown codes rather than guessing an exponent of 2 — guessing would silently mis-scale a real amount. Adding a currency is a one-line table edit.

The `Money.Domain.Money` namespace collides with the `Money` type name inside it. C# resolves this correctly in most positions but not all; test files in this plan alias it (`using MoneyValue = Money.Domain.Money.Money;`) where needed.

- [x] **Step 1: Write the failing tests**

```csharp
using Money.Domain.Money;

namespace Money.Domain.Tests.Money;

public sealed class CurrencyTests
{
    [Fact]
    public void A_known_code_resolves_with_its_minor_unit_exponent()
    {
        Currency.FromCode("EUR").Value.MinorUnitExponent.Should().Be(2);
        Currency.FromCode("JPY").Value.MinorUnitExponent.Should().Be(0);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData(" EUR ")]
    public void Lookup_is_case_and_whitespace_insensitive(string input)
    {
        Currency.FromCode(input).Value.Code.Should().Be("EUR");
    }

    [Fact]
    public void An_unknown_code_is_rejected_rather_than_assumed_to_have_two_decimals()
    {
        var result = Currency.FromCode("XYZ");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("currency.unknown");
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("")]
    public void A_malformed_code_is_rejected(string input)
    {
        Currency.FromCode(input).Error!.Code.Should().Be("currency.invalid_code");
    }

    [Fact]
    public void The_minor_unit_scale_is_ten_to_the_exponent()
    {
        Currency.FromCode("EUR").Value.MinorUnitScale.Should().Be(100m);
        Currency.FromCode("JPY").Value.MinorUnitScale.Should().Be(1m);
    }

    [Fact]
    public void Currencies_compare_by_value()
    {
        Currency.FromCode("EUR").Value.Should().Be(Currency.Eur);
    }

    [Fact]
    public void An_exponent_outside_the_supported_range_is_rejected()
    {
        Currency.Create("EUR", 9).Error!.Code.Should().Be("currency.invalid_minor_unit_exponent");
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter CurrencyTests`
Expected: FAIL — `Currency` does not exist.

- [x] **Step 3: Implement `Currency`**

```csharp
using Money.Domain.Primitives;

namespace Money.Domain.Money;

public sealed record Currency
{
    private const int MaxExponent = 4;

    private Currency(string code, int minorUnitExponent)
    {
        Code = code;
        MinorUnitExponent = minorUnitExponent;
    }

    public string Code { get; }

    public int MinorUnitExponent { get; }

    public decimal MinorUnitScale => Pow10(MinorUnitExponent);

    public static Currency Eur { get; } = new("EUR", 2);
    public static Currency Huf { get; } = new("HUF", 2);
    public static Currency Usd { get; } = new("USD", 2);

    private static readonly Dictionary<string, Currency> KnownByCode = new(StringComparer.Ordinal)
    {
        ["EUR"] = Eur,
        ["HUF"] = Huf,
        ["USD"] = Usd,
        ["GBP"] = new("GBP", 2),
        ["CHF"] = new("CHF", 2),
        ["CZK"] = new("CZK", 2),
        ["PLN"] = new("PLN", 2),
        ["RON"] = new("RON", 2),
        ["SEK"] = new("SEK", 2),
        ["DKK"] = new("DKK", 2),
        ["NOK"] = new("NOK", 2),
        ["JPY"] = new("JPY", 0),
    };

    public static IReadOnlyCollection<Currency> Known => KnownByCode.Values;

    public static Result<Currency> FromCode(string code)
    {
        var normalised = Normalise(code);
        if (normalised is null) return DomainErrors.Currency.InvalidCode(code ?? "");

        return KnownByCode.TryGetValue(normalised, out var currency)
            ? Result<Currency>.Ok(currency)
            : DomainErrors.Currency.Unknown(normalised);
    }

    /// <summary>Creates a currency outside the known table. For tests and future import paths.</summary>
    public static Result<Currency> Create(string code, int minorUnitExponent)
    {
        var normalised = Normalise(code);
        if (normalised is null) return DomainErrors.Currency.InvalidCode(code ?? "");
        if (minorUnitExponent is < 0 or > MaxExponent)
            return DomainErrors.Currency.InvalidMinorUnitExponent(minorUnitExponent);

        return Result<Currency>.Ok(new Currency(normalised, minorUnitExponent));
    }

    private static string? Normalise(string? code)
    {
        var trimmed = code?.Trim();
        if (trimmed is not { Length: 3 }) return null;
        if (!trimmed.All(char.IsAsciiLetter)) return null;
        return trimmed.ToUpperInvariant();
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10m;
        return result;
    }

    public override string ToString() => Code;
}
```

`Pow10` is hand-rolled because `Math.Pow` returns `double`, which the domain-purity architecture test forbids — and rightly so.

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter CurrencyTests`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/Money.Domain/Money tests/Money.Domain.Tests/Money
git commit -m "feat: add Currency with a known-currency table and no exponent guessing"
```

---

### Task 5: `Money`

**Files:**
- Create: `src/Money.Domain/Money/Money.cs`, `src/Money.Domain/Money/CurrencyMismatchException.cs`
- Test: `tests/Money.Domain.Tests/Money/MoneyTests.cs`

**Interfaces:**
- Consumes: `Currency`.
- Produces: `sealed record Money` with `long AmountMinor`, `Currency Currency`, `bool IsZero`, `int Sign`, `static Money Of(long, Currency)`, `static Money Zero(Currency)`, `Add`, `Subtract`, `Negate`, operators `+`, `-`, unary `-`, `Money ApplyRate(decimal)`, `static long RoundToMinor(decimal)`, `decimal ToDecimal()`.
- `RoundToMinor` is the solution's **single** rounding function — half away from zero. The accrual engine in phase 6 depends on it living here and nowhere else.

- [x] **Step 1: Write the failing tests**

```csharp
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Money;

public sealed class MoneyTests
{
    private static readonly Currency Eur = Currency.Eur;
    private static readonly Currency Usd = Currency.Usd;

    [Fact]
    public void Addition_and_subtraction_work_in_minor_units()
    {
        (MoneyValue.Of(2000, Eur) + MoneyValue.Of(345, Eur)).AmountMinor.Should().Be(2345);
        (MoneyValue.Of(2000, Eur) - MoneyValue.Of(345, Eur)).AmountMinor.Should().Be(1655);
    }

    [Fact]
    public void Arithmetic_between_different_currencies_throws()
    {
        var act = () => _ = MoneyValue.Of(100, Eur) + MoneyValue.Of(100, Usd);

        act.Should().Throw<CurrencyMismatchException>().WithMessage("*EUR*USD*");
    }

    [Fact]
    public void Negation_flips_the_sign_and_keeps_the_currency()
    {
        var negated = -MoneyValue.Of(2500, Eur);

        negated.AmountMinor.Should().Be(-2500);
        negated.Currency.Should().Be(Eur);
    }

    [Fact]
    public void Zero_is_zero_in_its_currency()
    {
        MoneyValue.Zero(Eur).IsZero.Should().BeTrue();
        MoneyValue.Zero(Eur).Sign.Should().Be(0);
        MoneyValue.Of(-1, Eur).Sign.Should().Be(-1);
        MoneyValue.Of(1, Eur).Sign.Should().Be(1);
    }

    [Fact]
    public void Conversion_to_decimal_divides_by_the_minor_unit_scale()
    {
        MoneyValue.Of(2345, Eur).ToDecimal().Should().Be(23.45m);
        MoneyValue.Of(2345, Currency.FromCode("JPY").Value).ToDecimal().Should().Be(2345m);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(-0.5, -1)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    [InlineData(-2.5, -3)]
    [InlineData(0.4999, 0)]
    public void Rounding_is_half_away_from_zero(decimal input, long expected)
    {
        MoneyValue.RoundToMinor(input).Should().Be(expected);
    }

    [Fact]
    public void Applying_a_rate_rounds_only_at_the_end()
    {
        MoneyValue.Of(10_000, Eur).ApplyRate(0.0375m).AmountMinor.Should().Be(375);
        MoneyValue.Of(333, Eur).ApplyRate(0.33333m).AmountMinor.Should().Be(111);
    }

    [Fact]
    public void Overflow_is_detected_rather_than_wrapping_silently()
    {
        var act = () => _ = MoneyValue.Of(long.MaxValue, Eur) + MoneyValue.Of(1, Eur);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Money_compares_by_value()
    {
        MoneyValue.Of(100, Eur).Should().Be(MoneyValue.Of(100, Eur));
        MoneyValue.Of(100, Eur).Should().NotBe(MoneyValue.Of(100, Usd));
    }

    [Fact]
    public void The_string_form_shows_the_major_amount_and_the_code()
    {
        MoneyValue.Of(-2345, Eur).ToString().Should().Be("-23.45 EUR");
        MoneyValue.Of(2345, Currency.FromCode("JPY").Value).ToString().Should().Be("2345 JPY");
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter MoneyTests`
Expected: FAIL — `Money` does not exist.

- [x] **Step 3: Implement `CurrencyMismatchException`**

```csharp
namespace Money.Domain.Money;

/// <summary>
/// Thrown when arithmetic mixes currencies. A programmer error, not a user error,
/// so it throws rather than returning a Result.
/// </summary>
public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot combine {left.Code} and {right.Code} amounts.")
    {
        Left = left;
        Right = right;
    }

    public Currency Left { get; }

    public Currency Right { get; }
}
```

- [x] **Step 4: Implement `Money`**

```csharp
using System.Globalization;

namespace Money.Domain.Money;

public sealed record Money
{
    private Money(long amountMinor, Currency currency)
    {
        AmountMinor = amountMinor;
        Currency = currency;
    }

    public long AmountMinor { get; }

    public Currency Currency { get; }

    public bool IsZero => AmountMinor == 0;

    public int Sign => Math.Sign(AmountMinor);

    public static Money Of(long amountMinor, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(amountMinor, currency);
    }

    public static Money Zero(Currency currency) => Of(0, currency);

    public Money Add(Money other)
    {
        RequireSameCurrency(other);
        return new Money(checked(AmountMinor + other.AmountMinor), Currency);
    }

    public Money Subtract(Money other)
    {
        RequireSameCurrency(other);
        return new Money(checked(AmountMinor - other.AmountMinor), Currency);
    }

    public Money Negate() => new(checked(-AmountMinor), Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    /// <summary>Multiplies by an exact decimal factor and rounds once, at the end.</summary>
    public Money ApplyRate(decimal factor) => new(RoundToMinor(AmountMinor * factor), Currency);

    /// <summary>The solution's only rounding function: half away from zero, to a whole minor unit.</summary>
    public static long RoundToMinor(decimal exactMinorUnits) =>
        checked((long)Math.Round(exactMinorUnits, 0, MidpointRounding.AwayFromZero));

    /// <summary>For display and rate arithmetic only. Never round-trip money through this.</summary>
    public decimal ToDecimal() => AmountMinor / Currency.MinorUnitScale;

    private void RequireSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!Currency.Equals(other.Currency)) throw new CurrencyMismatchException(Currency, other.Currency);
    }

    public override string ToString() =>
        ToDecimal().ToString("F" + Currency.MinorUnitExponent.ToString(CultureInfo.InvariantCulture),
                             CultureInfo.InvariantCulture)
        + " " + Currency.Code;
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter MoneyTests`
Expected: PASS.

- [x] **Step 6: Run the architecture tests**

Run: `dotnet test tests/Money.Architecture.Tests`
Expected: PASS — in particular `No_floating_point_types_appear_in_the_domain`, which is why `Math.Pow` and `double` were avoided in Tasks 4 and 5.

- [x] **Step 7: Commit**

```bash
git add src/Money.Domain/Money tests/Money.Domain.Tests/Money
git commit -m "feat: add Money value object with checked arithmetic and half-away-from-zero rounding"
```

---

### Task 6: `IClock`, `SystemClock`, `FakeClock`

**Files:**
- Create: `src/Money.Domain/Time/IClock.cs`, `src/Money.Infrastructure/Time/SystemClock.cs`, `tests/Money.TestSupport/FakeClock.cs`
- Test: `tests/Money.Domain.Tests/Time/FakeClockTests.cs`

**Interfaces:**
- Produces:
  - `interface IClock { DateTimeOffset UtcNow { get; } }`
  - `sealed class SystemClock : IClock` — the only place in `src/` allowed to read `DateTimeOffset.UtcNow`.
  - `sealed class FakeClock : IClock` with a settable `UtcNow`, `void Advance(TimeSpan)`, `static FakeClock At(int y, int m, int d)` and `static FakeClock At(int y, int m, int d, int hour, int minute)`.
- Every later task injects `IClock`. Nothing converts UTC to a local date except `PeriodResolver.TodayIn(IClock)` (Task 8).

- [x] **Step 1: Write the failing test**

```csharp
using Money.TestSupport;

namespace Money.Domain.Tests.Time;

public sealed class FakeClockTests
{
    [Fact]
    public void A_fake_clock_holds_still_until_advanced()
    {
        var clock = FakeClock.At(2026, 9, 1);

        var first = clock.UtcNow;
        var second = clock.UtcNow;

        second.Should().Be(first);
        first.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Advancing_moves_the_clock_by_exactly_the_requested_amount()
    {
        var clock = FakeClock.At(2026, 9, 1);

        clock.Advance(TimeSpan.FromHours(36));

        clock.UtcNow.Should().Be(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Money.Domain.Tests --filter FakeClockTests`
Expected: FAIL — `FakeClock` does not exist.

- [x] **Step 3: Implement `IClock`**

```csharp
namespace Money.Domain.Time;

/// <summary>
/// The only source of time in the solution. Ambient time is banned by an architecture test.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

- [x] **Step 4: Implement `SystemClock`**

```csharp
using Money.Domain.Time;

namespace Money.Infrastructure.Time;

/// <summary>
/// Composition root. This file is on the architecture test's allow-list for ambient time;
/// nothing else in src/ may read the system clock.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

- [x] **Step 5: Implement `FakeClock`**

```csharp
using Money.Domain.Time;

namespace Money.TestSupport;

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }

    public static FakeClock At(int year, int month, int day) =>
        new(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));

    public static FakeClock At(int year, int month, int day, int hour, int minute) =>
        new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
```

`Money.TestSupport` is a plain class library: no test SDK, no xUnit, no `IsTestProject`.

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter FakeClockTests`
Expected: PASS.

- [x] **Step 7: Run the architecture tests**

Run: `dotnet test tests/Money.Architecture.Tests --filter Ambient_time`
Expected: PASS — `SystemClock.cs` is on the allow-list and is the only reader of ambient time in `src/`.

- [x] **Step 8: Commit**

```bash
git add src/Money.Domain/Time src/Money.Infrastructure/Time tests/Money.TestSupport tests/Money.Domain.Tests/Time
git commit -m "feat: add IClock with SystemClock and FakeClock implementations"
```

---

### Task 7: Period vocabulary — `PeriodType`, `PeriodAnchor`, `PeriodDefinition`, `PeriodKey`, `DateRange`

**Files:**
- Create: `src/Money.Domain/Periods/PeriodType.cs`, `PeriodAnchor.cs`, `PeriodDefinition.cs`, `PeriodKey.cs`, `DateRange.cs`
- Test: `tests/Money.Domain.Tests/Periods/PeriodKeyTests.cs`, `PeriodDefinitionTests.cs`, `DateRangeTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `DomainErrors.Period`.
- Produces:
  - `enum PeriodType { Weekly = 1, Monthly = 2, Quarterly = 3, Yearly = 4 }`
  - `abstract record PeriodAnchor` with `static PeriodAnchor CalendarMonth`, `static Result<PeriodAnchor> DayOfMonth(int day)`, `int AnchorDay`, and nested `sealed record CalendarMonthAnchor` / `sealed record DayOfMonthAnchor(int Day)`
  - `sealed record PeriodDefinition` with `PeriodAnchor Anchor`, `string TimeZoneId`, `DayOfWeek FirstDayOfWeek`, `static PeriodDefinition Default`, `static Result<PeriodDefinition> Create(PeriodAnchor, string, DayOfWeek)`, `internal static bool TryFindTimeZone(string, out TimeZoneInfo)`
  - `readonly record struct PeriodKey` with `PeriodType Type`, `int Year`, `int Index`, `static Result<PeriodKey> Create(...)`, `string ToKeyString()`, `static Result<PeriodKey> Parse(string)`
  - `readonly record struct DateRange` with `DateOnly Start`, `DateOnly EndExclusive`, `int LengthInDays`, `bool Contains(DateOnly)`, `static Result<DateRange> Create(...)`, `internal static DateRange FromOrdered(...)`

- [x] **Step 1: Write the failing `PeriodKey` tests**

```csharp
using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

public sealed class PeriodKeyTests
{
    [Theory]
    [InlineData(PeriodType.Monthly, 2026, 9, "2026-M09")]
    [InlineData(PeriodType.Weekly, 2026, 36, "2026-W36")]
    [InlineData(PeriodType.Quarterly, 2026, 3, "2026-Q3")]
    [InlineData(PeriodType.Yearly, 2026, 1, "2026-Y")]
    public void A_key_renders_in_its_documented_string_form(
        PeriodType type, int year, int index, string expected)
    {
        PeriodKey.Create(type, year, index).Value.ToKeyString().Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-M09", PeriodType.Monthly, 2026, 9)]
    [InlineData("2026-W36", PeriodType.Weekly, 2026, 36)]
    [InlineData("2026-Q3", PeriodType.Quarterly, 2026, 3)]
    [InlineData("2026-Y", PeriodType.Yearly, 2026, 1)]
    public void Parsing_is_the_inverse_of_rendering(string text, PeriodType type, int year, int index)
    {
        PeriodKey.Parse(text).Value.Should().Be(PeriodKey.Create(type, year, index).Value);
    }

    [Theory]
    [InlineData(PeriodType.Monthly, 0)]
    [InlineData(PeriodType.Monthly, 13)]
    [InlineData(PeriodType.Quarterly, 5)]
    [InlineData(PeriodType.Weekly, 54)]
    [InlineData(PeriodType.Yearly, 2)]
    public void An_index_outside_its_type_s_range_is_rejected(PeriodType type, int index)
    {
        PeriodKey.Create(type, 2026, index).Error!.Code.Should().Be("period.index_out_of_range");
    }

    [Theory]
    [InlineData("2026-X09")]
    [InlineData("nonsense")]
    [InlineData("2026-M99")]
    [InlineData("2026-Y1")]
    [InlineData("")]
    public void An_unparsable_key_is_rejected(string text)
    {
        PeriodKey.Parse(text).IsFailure.Should().BeTrue();
    }
}
```

- [x] **Step 2: Write the failing `PeriodDefinition` and `DateRange` tests**

```csharp
using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

public sealed class PeriodDefinitionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(28)]
    public void An_anchor_day_within_one_to_twenty_eight_is_accepted(int day)
    {
        PeriodAnchor.DayOfMonth(day).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(31)]
    public void An_anchor_day_above_twenty_eight_is_rejected_with_an_explanation(int day)
    {
        var result = PeriodAnchor.DayOfMonth(day);

        result.Error!.Code.Should().Be("period.anchor_day_out_of_range");
        result.Error.Message.Should().Contain("not exist in every month");
    }

    [Fact]
    public void The_calendar_month_anchor_reports_day_one()
    {
        PeriodAnchor.CalendarMonth.AnchorDay.Should().Be(1);
        PeriodAnchor.DayOfMonth(25).Value.AnchorDay.Should().Be(25);
    }

    [Fact]
    public void A_definition_requires_a_time_zone_this_machine_knows()
    {
        PeriodDefinition.Create(PeriodAnchor.CalendarMonth, "Mars/Olympus", DayOfWeek.Monday)
            .Error!.Code.Should().Be("period.unknown_time_zone");
    }

    [Fact]
    public void An_iana_time_zone_id_is_accepted()
    {
        PeriodDefinition.Create(PeriodAnchor.CalendarMonth, "Europe/Budapest", DayOfWeek.Monday)
            .IsSuccess.Should().BeTrue();
    }
}
```

```csharp
using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

public sealed class DateRangeTests
{
    [Fact]
    public void A_range_includes_its_start_and_excludes_its_end()
    {
        var range = DateRange.Create(new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 25)).Value;

        range.Contains(new DateOnly(2026, 9, 25)).Should().BeTrue();
        range.Contains(new DateOnly(2026, 10, 24)).Should().BeTrue();
        range.Contains(new DateOnly(2026, 10, 25)).Should().BeFalse();
        range.Contains(new DateOnly(2026, 9, 24)).Should().BeFalse();
    }

    [Fact]
    public void A_range_reports_its_length_in_days()
    {
        DateRange.Create(new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 25)).Value
            .LengthInDays.Should().Be(30);
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_rejected()
    {
        DateRange.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1))
            .Error!.Code.Should().Be("period.end_before_start");
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter Periods`
Expected: FAIL — the `Money.Domain.Periods` namespace does not exist.

- [x] **Step 4: Implement the period vocabulary**

`PeriodType.cs` — explicit numeric values because these are persisted:

```csharp
namespace Money.Domain.Periods;

public enum PeriodType
{
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4
}
```

`PeriodAnchor.cs`:

```csharp
using Money.Domain.Primitives;

namespace Money.Domain.Periods;

public abstract record PeriodAnchor
{
    private PeriodAnchor() { }

    public static PeriodAnchor CalendarMonth { get; } = new CalendarMonthAnchor();

    /// <summary>
    /// Days above 28 are rejected: not every month has them, so a period boundary
    /// would need clamping and would stop tiling cleanly.
    /// </summary>
    public static Result<PeriodAnchor> DayOfMonth(int day) =>
        day is >= 1 and <= 28
            ? Result<PeriodAnchor>.Ok(new DayOfMonthAnchor(day))
            : DomainErrors.Period.AnchorDayOutOfRange(day);

    public int AnchorDay => this switch
    {
        CalendarMonthAnchor => 1,
        DayOfMonthAnchor d => d.Day,
        _ => throw new NotSupportedException($"Unhandled anchor {GetType().Name}.")
    };

    public sealed record CalendarMonthAnchor : PeriodAnchor;

    public sealed record DayOfMonthAnchor(int Day) : PeriodAnchor;
}
```

`PeriodDefinition.cs`:

```csharp
using Money.Domain.Primitives;

namespace Money.Domain.Periods;

public sealed record PeriodDefinition
{
    private PeriodDefinition(PeriodAnchor anchor, string timeZoneId, DayOfWeek firstDayOfWeek)
    {
        Anchor = anchor;
        TimeZoneId = timeZoneId;
        FirstDayOfWeek = firstDayOfWeek;
    }

    public PeriodAnchor Anchor { get; }

    public string TimeZoneId { get; }

    public DayOfWeek FirstDayOfWeek { get; }

    public static PeriodDefinition Default { get; } =
        new(PeriodAnchor.CalendarMonth, "Europe/Budapest", DayOfWeek.Monday);

    public static Result<PeriodDefinition> Create(
        PeriodAnchor anchor, string timeZoneId, DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        if (!TryFindTimeZone(timeZoneId, out _)) return DomainErrors.Period.UnknownTimeZone(timeZoneId ?? "");

        return Result<PeriodDefinition>.Ok(new PeriodDefinition(anchor, timeZoneId, firstDayOfWeek));
    }

    internal static bool TryFindTimeZone(string? id, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id)) return false;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException
                                      or ArgumentException)
        {
            return false;
        }
    }
}
```

`PeriodKey.cs`:

```csharp
using System.Globalization;
using Money.Domain.Primitives;

namespace Money.Domain.Periods;

public readonly record struct PeriodKey
{
    private PeriodKey(PeriodType type, int year, int index)
    {
        Type = type;
        Year = year;
        Index = index;
    }

    public PeriodType Type { get; }

    public int Year { get; }

    public int Index { get; }

    public static Result<PeriodKey> Create(PeriodType type, int year, int index)
    {
        if (year is < 1 or > 9999) return DomainErrors.Period.IndexOutOfRange(type.ToString(), year);

        var maxIndex = type switch
        {
            PeriodType.Weekly => 53,
            PeriodType.Monthly => 12,
            PeriodType.Quarterly => 4,
            PeriodType.Yearly => 1,
            _ => throw new NotSupportedException($"Unhandled period type {type}.")
        };

        return index >= 1 && index <= maxIndex
            ? Result<PeriodKey>.Ok(new PeriodKey(type, year, index))
            : DomainErrors.Period.IndexOutOfRange(type.ToString(), index);
    }

    public string ToKeyString() => Type switch
    {
        PeriodType.Weekly => $"{Year:D4}-W{Index:D2}",
        PeriodType.Monthly => $"{Year:D4}-M{Index:D2}",
        PeriodType.Quarterly => $"{Year:D4}-Q{Index:D1}",
        PeriodType.Yearly => $"{Year:D4}-Y",
        _ => throw new NotSupportedException($"Unhandled period type {Type}.")
    };

    public static Result<PeriodKey> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DomainErrors.Period.UnparsableKey(text ?? "");

        var trimmed = text.Trim();
        if (trimmed.Length < 6 || trimmed[4] != '-') return DomainErrors.Period.UnparsableKey(trimmed);

        if (!int.TryParse(trimmed.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            return DomainErrors.Period.UnparsableKey(trimmed);

        var type = trimmed[5] switch
        {
            'W' => PeriodType.Weekly,
            'M' => PeriodType.Monthly,
            'Q' => PeriodType.Quarterly,
            'Y' => PeriodType.Yearly,
            _ => (PeriodType?)null
        };

        if (type is null) return DomainErrors.Period.UnparsableKey(trimmed);

        if (type == PeriodType.Yearly)
        {
            return trimmed.Length == 6
                ? Create(PeriodType.Yearly, year, 1)
                : DomainErrors.Period.UnparsableKey(trimmed);
        }

        if (!int.TryParse(trimmed.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return DomainErrors.Period.UnparsableKey(trimmed);

        return Create(type.Value, year, index);
    }

    public override string ToString() => ToKeyString();
}
```

`Create` reuses `IndexOutOfRange` for a bad *year* deliberately rather than adding a near-duplicate code; the message names the offending number.

`DateRange.cs`:

```csharp
using Money.Domain.Primitives;

namespace Money.Domain.Periods;

/// <summary>A half-open date interval: [Start, EndExclusive).</summary>
public readonly record struct DateRange
{
    private DateRange(DateOnly start, DateOnly endExclusive)
    {
        Start = start;
        EndExclusive = endExclusive;
    }

    public DateOnly Start { get; }

    public DateOnly EndExclusive { get; }

    public int LengthInDays => EndExclusive.DayNumber - Start.DayNumber;

    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;

    public static Result<DateRange> Create(DateOnly start, DateOnly endExclusive) =>
        endExclusive > start
            ? Result<DateRange>.Ok(new DateRange(start, endExclusive))
            : DomainErrors.Period.EndBeforeStart(start, endExclusive);

    /// <summary>For callers that have already established ordering (PeriodResolver).</summary>
    internal static DateRange FromOrdered(DateOnly start, DateOnly endExclusive) => new(start, endExclusive);

    public override string ToString() => $"[{Start:O}, {EndExclusive:O})";
}
```

`FromOrdered` is `internal`, so `Money.Domain.Tests` needs `InternalsVisibleTo`. Add to `src/Money.Domain/Money.Domain.csproj`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Money.Domain.Tests" />
  </ItemGroup>
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter Periods`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/Money.Domain tests/Money.Domain.Tests/Periods
git commit -m "feat: add period vocabulary - type, anchor, definition, key and date range"
```

---

### Task 8: `PeriodResolver` and the I9 tiling property test

**Files:**
- Create: `src/Money.Domain/Periods/PeriodResolver.cs`
- Test: `tests/Money.Domain.Tests/Periods/PeriodResolverTests.cs`, `PeriodTilingPropertyTests.cs`

**Interfaces:**
- Consumes: `PeriodDefinition`, `PeriodKey`, `DateRange`, `IClock`.
- Produces: `sealed class PeriodResolver` — `PeriodResolver(PeriodDefinition)`, `PeriodDefinition Definition`, `PeriodKey Resolve(DateOnly, PeriodType)`, `DateRange Range(PeriodKey)`, `PeriodKey Next(PeriodKey)`, `PeriodKey Previous(PeriodKey)`, `DateOnly TodayIn(IClock)`.
- **Nothing else in the solution computes a period boundary.** Budgets (phase 4), pockets (phase 5) and the dashboard (phase 7) all call this.

The week rule, stated once so it is not reinvented: a week starts on `FirstDayOfWeek`; a week belongs to the year of its **midweek day** (`weekStart + 3 days`); week 1 of a year is the week containing 4 January. With `FirstDayOfWeek = Monday` this is exactly ISO-8601.

- [x] **Step 1: Write the failing example tests**

```csharp
using Money.Domain.Periods;
using Money.TestSupport;

namespace Money.Domain.Tests.Periods;

public sealed class PeriodResolverTests
{
    private static PeriodResolver CalendarMonth() =>
        new(PeriodDefinition.Create(PeriodAnchor.CalendarMonth, "Europe/Budapest", DayOfWeek.Monday).Value);

    private static PeriodResolver AnchoredOn(int day) =>
        new(PeriodDefinition.Create(PeriodAnchor.DayOfMonth(day).Value, "Europe/Budapest", DayOfWeek.Monday).Value);

    [Fact]
    public void A_calendar_month_period_runs_from_the_first_to_the_first()
    {
        var range = CalendarMonth().Range(PeriodKey.Create(PeriodType.Monthly, 2026, 9).Value);

        range.Start.Should().Be(new DateOnly(2026, 9, 1));
        range.EndExclusive.Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void A_period_anchored_on_day_twenty_five_is_labelled_by_the_month_it_starts_in()
    {
        // Spec 5.4: "the period from 25 September to 24 October is 2026-M09"
        var resolver = AnchoredOn(25);

        resolver.Resolve(new DateOnly(2026, 9, 25), PeriodType.Monthly).ToKeyString().Should().Be("2026-M09");
        resolver.Resolve(new DateOnly(2026, 10, 24), PeriodType.Monthly).ToKeyString().Should().Be("2026-M09");
        resolver.Resolve(new DateOnly(2026, 10, 25), PeriodType.Monthly).ToKeyString().Should().Be("2026-M10");
        resolver.Resolve(new DateOnly(2026, 9, 24), PeriodType.Monthly).ToKeyString().Should().Be("2026-M08");
    }

    [Fact]
    public void An_anchored_range_spans_two_calendar_months()
    {
        var range = AnchoredOn(25).Range(PeriodKey.Create(PeriodType.Monthly, 2026, 9).Value);

        range.Start.Should().Be(new DateOnly(2026, 9, 25));
        range.EndExclusive.Should().Be(new DateOnly(2026, 10, 25));
    }

    [Fact]
    public void An_anchored_year_starts_on_the_anchor_day_of_january()
    {
        var resolver = AnchoredOn(25);

        resolver.Resolve(new DateOnly(2026, 1, 10), PeriodType.Yearly).Year.Should().Be(2025);
        resolver.Range(PeriodKey.Create(PeriodType.Yearly, 2026, 1).Value).Start
            .Should().Be(new DateOnly(2026, 1, 25));
    }

    [Fact]
    public void An_anchored_quarter_starts_on_the_anchor_day_of_the_quarter_s_first_month()
    {
        var range = AnchoredOn(25).Range(PeriodKey.Create(PeriodType.Quarterly, 2026, 4).Value);

        range.Start.Should().Be(new DateOnly(2026, 10, 25));
        range.EndExclusive.Should().Be(new DateOnly(2027, 1, 25));
    }

    [Fact]
    public void Weeks_follow_iso_8601_when_the_week_starts_on_monday()
    {
        var resolver = CalendarMonth();

        // 2026-01-01 is a Thursday, so it is in ISO week 1 of 2026,
        // and 2025-12-29 is the Monday that starts that same week.
        resolver.Resolve(new DateOnly(2026, 1, 1), PeriodType.Weekly).ToKeyString().Should().Be("2026-W01");
        resolver.Resolve(new DateOnly(2025, 12, 29), PeriodType.Weekly).ToKeyString().Should().Be("2026-W01");
        resolver.Range(PeriodKey.Create(PeriodType.Weekly, 2026, 1).Value).Start
            .Should().Be(new DateOnly(2025, 12, 29));
    }

    [Fact]
    public void Next_and_previous_walk_the_timeline_without_gaps()
    {
        var resolver = AnchoredOn(25);
        var september = PeriodKey.Create(PeriodType.Monthly, 2026, 9).Value;

        var october = resolver.Next(september);

        october.ToKeyString().Should().Be("2026-M10");
        resolver.Range(october).Start.Should().Be(resolver.Range(september).EndExclusive);
        resolver.Previous(october).Should().Be(september);
    }

    [Fact]
    public void Next_rolls_over_the_year_boundary()
    {
        var resolver = CalendarMonth();

        resolver.Next(PeriodKey.Create(PeriodType.Monthly, 2026, 12).Value)
            .ToKeyString().Should().Be("2027-M01");
        resolver.Previous(PeriodKey.Create(PeriodType.Monthly, 2026, 1).Value)
            .ToKeyString().Should().Be("2025-M12");
    }

    [Fact]
    public void Today_is_the_date_in_the_configured_time_zone_not_in_utc()
    {
        // 23:30 UTC on 31 August is already 01:30 on 1 September in Budapest (UTC+2 in summer).
        CalendarMonth().TodayIn(FakeClock.At(2026, 8, 31, 23, 30))
            .Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void A_month_containing_a_dst_change_still_has_its_calendar_length()
    {
        // Central European DST ends on the last Sunday of October; period maths is date-only,
        // so the month is still 31 days long.
        var resolver = CalendarMonth();

        resolver.Resolve(new DateOnly(2026, 10, 25), PeriodType.Monthly).ToKeyString().Should().Be("2026-M10");
        resolver.Range(PeriodKey.Create(PeriodType.Monthly, 2026, 10).Value).LengthInDays.Should().Be(31);
    }

    [Fact]
    public void A_week_index_that_does_not_exist_in_that_year_is_a_programmer_error()
    {
        // 2026 has 53 ISO weeks; 2027 has 52.
        var resolver = CalendarMonth();

        var act = () => resolver.Range(PeriodKey.Create(PeriodType.Weekly, 2027, 53).Value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_february_period_is_shorter_than_a_march_one()
    {
        var resolver = CalendarMonth();

        resolver.Range(PeriodKey.Create(PeriodType.Monthly, 2027, 2).Value).LengthInDays.Should().Be(28);
        resolver.Range(PeriodKey.Create(PeriodType.Monthly, 2028, 2).Value).LengthInDays.Should().Be(29);
    }
}
```

- [x] **Step 2: Write the failing I9 property test**

```csharp
using CsCheck;
using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

/// <summary>
/// Invariant I9: periods of one PeriodType tile the timeline - no gaps, no overlaps.
/// </summary>
public sealed class PeriodTilingPropertyTests
{
    private static readonly Gen<DateOnly> AnyDate =
        Gen.Int[new DateOnly(2000, 1, 1).DayNumber, new DateOnly(2060, 12, 31).DayNumber]
           .Select(DateOnly.FromDayNumber);

    private static readonly Gen<PeriodType> AnyType =
        Gen.OneOfConst(PeriodType.Weekly, PeriodType.Monthly, PeriodType.Quarterly, PeriodType.Yearly);

    private static readonly Gen<int> AnyAnchorDay = Gen.Int[1, 28];

    private static readonly Gen<DayOfWeek> AnyFirstDayOfWeek =
        Gen.OneOfConst(DayOfWeek.Monday, DayOfWeek.Sunday, DayOfWeek.Saturday);

    private static PeriodResolver ResolverFor(int anchorDay, DayOfWeek firstDayOfWeek) =>
        new(PeriodDefinition.Create(
                PeriodAnchor.DayOfMonth(anchorDay).Value, "Europe/Budapest", firstDayOfWeek).Value);

    [Fact]
    public void Every_date_falls_inside_the_period_it_resolves_to()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchorDay, AnyFirstDayOfWeek)
           .Sample((date, type, anchorDay, firstDay) =>
           {
               var resolver = ResolverFor(anchorDay, firstDay);
               return resolver.Range(resolver.Resolve(date, type)).Contains(date);
           }, iter: 10_000);
    }

    [Fact]
    public void Consecutive_periods_share_a_boundary_exactly()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchorDay, AnyFirstDayOfWeek)
           .Sample((date, type, anchorDay, firstDay) =>
           {
               var resolver = ResolverFor(anchorDay, firstDay);
               var key = resolver.Resolve(date, type);
               return resolver.Range(resolver.Next(key)).Start == resolver.Range(key).EndExclusive;
           }, iter: 10_000);
    }

    [Fact]
    public void Previous_undoes_next()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchorDay, AnyFirstDayOfWeek)
           .Sample((date, type, anchorDay, firstDay) =>
           {
               var resolver = ResolverFor(anchorDay, firstDay);
               var key = resolver.Resolve(date, type);
               return resolver.Previous(resolver.Next(key)) == key;
           }, iter: 10_000);
    }

    [Fact]
    public void The_day_before_a_period_belongs_to_the_previous_period_and_no_other()
    {
        Gen.Select(AnyDate, AnyType, AnyAnchorDay, AnyFirstDayOfWeek)
           .Sample((date, type, anchorDay, firstDay) =>
           {
               var resolver = ResolverFor(anchorDay, firstDay);
               var key = resolver.Resolve(date, type);
               var dayBefore = resolver.Range(key).Start.AddDays(-1);
               return resolver.Resolve(dayBefore, type) == resolver.Previous(key);
           }, iter: 10_000);
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter PeriodResolver`
Expected: FAIL — `PeriodResolver` does not exist.

- [x] **Step 4: Implement `PeriodResolver`**

```csharp
using Money.Domain.Time;

namespace Money.Domain.Periods;

/// <summary>
/// The single component that computes period boundaries. Payday-anchored budgeting stays a
/// setting rather than a rewrite only because nothing else does month arithmetic.
/// </summary>
public sealed class PeriodResolver
{
    private readonly PeriodDefinition _definition;
    private readonly int _anchorDay;
    private readonly TimeZoneInfo _timeZone;

    public PeriodResolver(PeriodDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
        _anchorDay = definition.Anchor.AnchorDay;

        if (!PeriodDefinition.TryFindTimeZone(definition.TimeZoneId, out var timeZone))
        {
            throw new ArgumentException(
                $"Time zone '{definition.TimeZoneId}' is unknown. PeriodDefinition.Create validates this; " +
                "a resolver must never be built from an unvalidated definition.", nameof(definition));
        }

        _timeZone = timeZone;
    }

    public PeriodDefinition Definition => _definition;

    /// <summary>Today's date in the configured time zone. The only UTC-to-local conversion in the domain.</summary>
    public DateOnly TodayIn(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, _timeZone).DateTime);
    }

    public PeriodKey Resolve(DateOnly date, PeriodType type) => type switch
    {
        PeriodType.Weekly => ResolveWeekly(date),
        PeriodType.Monthly => ResolveMonthly(date),
        PeriodType.Quarterly => ResolveQuarterly(date),
        PeriodType.Yearly => ResolveYearly(date),
        _ => throw new NotSupportedException($"Unhandled period type {type}.")
    };

    public DateRange Range(PeriodKey key) => key.Type switch
    {
        PeriodType.Weekly => WeeklyRange(key),
        PeriodType.Monthly => MonthlyRange(key),
        PeriodType.Quarterly => QuarterlyRange(key),
        PeriodType.Yearly => YearlyRange(key),
        _ => throw new NotSupportedException($"Unhandled period type {key.Type}.")
    };

    public PeriodKey Next(PeriodKey key) => Resolve(Range(key).EndExclusive, key.Type);

    public PeriodKey Previous(PeriodKey key) => Resolve(Range(key).Start.AddDays(-1), key.Type);

    // ---- monthly, quarterly, yearly all sit on the anchored month ------------------

    /// <summary>The (year, month) whose anchored period contains <paramref name="date"/>.</summary>
    private (int Year, int Month) AnchoredMonth(DateOnly date) =>
        date.Day >= _anchorDay
            ? (date.Year, date.Month)
            : date.Month == 1 ? (date.Year - 1, 12) : (date.Year, date.Month - 1);

    private PeriodKey ResolveMonthly(DateOnly date)
    {
        var (year, month) = AnchoredMonth(date);
        return Key(PeriodType.Monthly, year, month);
    }

    private DateRange MonthlyRange(PeriodKey key)
    {
        var start = new DateOnly(key.Year, key.Index, _anchorDay);
        var (nextYear, nextMonth) = key.Index == 12 ? (key.Year + 1, 1) : (key.Year, key.Index + 1);
        return DateRange.FromOrdered(start, new DateOnly(nextYear, nextMonth, _anchorDay));
    }

    private PeriodKey ResolveQuarterly(DateOnly date)
    {
        var (year, month) = AnchoredMonth(date);
        return Key(PeriodType.Quarterly, year, ((month - 1) / 3) + 1);
    }

    private DateRange QuarterlyRange(PeriodKey key)
    {
        var startMonth = ((key.Index - 1) * 3) + 1;
        var start = new DateOnly(key.Year, startMonth, _anchorDay);
        var endMonth = startMonth + 3;
        var end = endMonth > 12
            ? new DateOnly(key.Year + 1, endMonth - 12, _anchorDay)
            : new DateOnly(key.Year, endMonth, _anchorDay);
        return DateRange.FromOrdered(start, end);
    }

    private PeriodKey ResolveYearly(DateOnly date) => Key(PeriodType.Yearly, AnchoredMonth(date).Year, 1);

    private DateRange YearlyRange(PeriodKey key) =>
        DateRange.FromOrdered(new DateOnly(key.Year, 1, _anchorDay),
                              new DateOnly(key.Year + 1, 1, _anchorDay));

    // ---- weekly -------------------------------------------------------------------

    private DateOnly WeekStart(DateOnly date)
    {
        var offset = (7 + (int)date.DayOfWeek - (int)_definition.FirstDayOfWeek) % 7;
        return date.AddDays(-offset);
    }

    private PeriodKey ResolveWeekly(DateOnly date)
    {
        var midweek = WeekStart(date).AddDays(3);
        return Key(PeriodType.Weekly, midweek.Year, ((midweek.DayOfYear - 1) / 7) + 1);
    }

    private DateRange WeeklyRange(PeriodKey key)
    {
        // Under the midweek rule the week containing 4 January is always week 1.
        var start = WeekStart(new DateOnly(key.Year, 1, 4)).AddDays(7 * (key.Index - 1));

        if (ResolveWeekly(start) != key)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key), key, $"Week {key.Index} does not exist in {key.Year}.");
        }

        return DateRange.FromOrdered(start, start.AddDays(7));
    }

    private static PeriodKey Key(PeriodType type, int year, int index) =>
        PeriodKey.Create(type, year, index).Value;
}
```

`DateRange.FromOrdered` is `internal`, so `Money.Domain` needs no extra visibility for its own use — but the `InternalsVisibleTo` added in Task 7 keeps the domain tests able to construct ranges directly if they need to.

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter Period`
Expected: PASS — including all four properties at 10,000 iterations each.

If `Previous_undoes_next` fails for `Weekly` near a year boundary, the bug is in `WeeklyRange`'s week-1 anchor, not in the property. Do not weaken the property.

- [x] **Step 6: Commit**

```bash
git add src/Money.Domain/Periods tests/Money.Domain.Tests/Periods
git commit -m "feat: add PeriodResolver with I9 tiling property tests"
```

---

### Task 9: Mutation-testing gate on `Money.Domain`

**Files:**
- Create: `stryker-config.json`, `.config/dotnet-tools.json`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: everything in `Money.Domain` so far.
- Produces: a CI job that fails when the domain's mutation score drops below 80. Spec D7: line coverage is not accepted as evidence for money logic.

Phases 1–8 must not lower the threshold. If a phase's code cannot reach it, the fix is better assertions, not a lower bar.

- [x] **Step 1: Install Stryker as a local tool**

```bash
dotnet new tool-manifest
dotnet tool install dotnet-stryker
```

- [x] **Step 2: Write `stryker-config.json`**

```json
{
  "stryker-config": {
    "project": "Money.Domain.csproj",
    "test-projects": [ "../../tests/Money.Domain.Tests/Money.Domain.Tests.csproj" ],
    "reporters": [ "progress", "html", "cleartext" ],
    "thresholds": { "high": 90, "low": 80, "break": 80 },
    "mutation-level": "Standard",
    "ignore-mutations": [ "string" ],
    "coverage-analysis": "perTest"
  }
}
```

`ignore-mutations: ["string"]` skips mutations of string literals — error messages and format strings — which produce noise without protecting money logic. Everything arithmetic, conditional and boundary-related stays mutated.

- [x] **Step 3: Run Stryker locally and read the report**

Run: `dotnet stryker` from `src/Money.Domain`
Expected: a score at or above 80. The HTML report lands in `StrykerOutput/`.

Survived mutants are a to-do list, not a formality. For each survivor in `Money`, `Currency`, `PeriodKey` or `PeriodResolver`, either add the assertion that kills it or record in the commit message why it is equivalent. Expected survivors to attack first:
- boundary mutations on the anchor-day range (`<= 28` → `< 28`) — killed by the `InlineData(28)` case, which is why it exists;
- `MidpointRounding.AwayFromZero` → `ToEven` — killed by the `2.5 → 3` and `-2.5 → -3` cases;
- the `+ 3` in the weekly midweek rule — killed by the ISO week-1 example test.

- [x] **Step 4: Add the mutation job to CI**

Append to `.github/workflows/ci.yml`:

```yaml
  mutation-test-domain:
    runs-on: ubuntu-latest
    needs: build-and-test
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - run: dotnet tool restore
      - name: Stryker on Money.Domain
        run: dotnet stryker --break-at 80
        working-directory: src/Money.Domain
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: stryker-report
          path: '**/StrykerOutput/**/reports/**'
```

- [x] **Step 5: Verify the gate actually fails when it should**

Temporarily delete the `[InlineData(-2.5, -3)]` case from `MoneyTests.Rounding_is_half_away_from_zero`, run `dotnet stryker`, confirm a `MidpointRounding` mutant survives and the score drops. Restore the case.

- [x] **Step 6: Commit**

```bash
git add .config stryker-config.json .github/workflows/ci.yml
git commit -m "chore: gate Money.Domain with Stryker mutation testing at score 80"
```

**Phase 0 acceptance:** `dotnet build -warnaserror`, `dotnet test` and `dotnet stryker` all pass; architecture tests pass; the I9 tiling property test passes at 10,000 iterations per property.

---

# Phase 1 — Ledger core

**Deliverable:** accounts, the category tree, transactions, postings, the balancing rule and balance queries — all pure, all in `Money.Domain`, no database anywhere near it.
**Acceptance:** I1–I5, I11 and I12 hold under property tests.

The whole point of this phase is that a transaction which does not balance **cannot be constructed**. Persistence in phase 2 then only has to store what the domain already refused to get wrong.

---

### Task 10: `Account` — kinds, roles and the materialised path

**Files:**
- Create: `src/Money.Domain/Accounts/AccountKind.cs`, `AccountRole.cs`, `Account.cs`
- Test: `tests/Money.Domain.Tests/Accounts/AccountCreationTests.cs`

**Interfaces:**
- Consumes: `Currency`, `Result<T>`, `DomainErrors.Account`.
- Produces:
  - `enum AccountKind { Asset = 1, Liability = 2, Income = 3, Expense = 4, Equity = 5 }`
  - `enum AccountRole { Bank = 1, Cash = 2, SavingsPocket = 3, Investment = 4, Category = 5, OpeningBalance = 6, Adjustment = 7 }`
  - `sealed class Account` with `Guid Id`, `string Name`, `AccountKind Kind`, `AccountRole Role`, `Guid? ParentAccountId`, `string Path`, `string CurrencyCode`, `bool IsArchived`, `DateOnly? OpenedOn`, `int SortOrder`, `string? ColorHex`, `string? Icon`, `string? Notes`, `DateTimeOffset CreatedAtUtc`, `DateTimeOffset UpdatedAtUtc`; `const int MaxNameLength = 120`; `string ChildPathPrefix`; `bool IsCategory`; `static Result<Account> Create(Guid id, string name, AccountKind kind, AccountRole role, Account? parent, Currency currency, DateTimeOffset nowUtc)`; `static string Slugify(string name)`; `static string RootPathFor(AccountKind kind)`.

**Consequence worth stating out loud:** the spec's role/kind rules leave `AccountKind.Liability` with no legal role, so a liability account cannot be created in v1. That is correct — credit cards are a non-goal (spec section 2) and the enum value exists only so the schema does not need a migration later. Do not invent a role to make it reachable.

- [x] **Step 1: Write the failing tests**

```csharp
using Money.Domain.Accounts;
using Money.Domain.Money;

namespace Money.Domain.Tests.Accounts;

public sealed class AccountCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static Account Root(string name, AccountKind kind, AccountRole role) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, parent: null, Currency.Eur, Now).Value;

    [Fact]
    public void A_root_account_s_path_is_its_kind_then_its_slug()
    {
        Root("Gaming", AccountKind.Expense, AccountRole.Category).Path.Should().Be("/expense/gaming");
        Root("Erste Current", AccountKind.Asset, AccountRole.Bank).Path.Should().Be("/asset/erste-current");
    }

    [Fact]
    public void A_child_account_s_path_extends_its_parent_s()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        var steam = Account.Create(Guid.CreateVersion7(Now), "Steam", AccountKind.Expense,
                                   AccountRole.Category, gaming, Currency.Eur, Now).Value;

        steam.Path.Should().Be("/expense/gaming/steam");
        steam.ParentAccountId.Should().Be(gaming.Id);
        gaming.ChildPathPrefix.Should().Be("/expense/gaming/");
    }

    [Theory]
    [InlineData("Eating out", "eating-out")]
    [InlineData("  Spaced  Name  ", "spaced-name")]
    [InlineData("Gaming / Steam", "gaming-steam")]
    [InlineData("Étterem", "étterem")]
    [InlineData("Rent (flat)", "rent-flat")]
    public void Slugs_are_lowercase_and_hyphenated(string name, string expectedSlug)
    {
        Root(name, AccountKind.Expense, AccountRole.Category).Path
            .Should().Be("/expense/" + expectedSlug);
    }

    [Fact]
    public void A_name_with_no_letters_or_digits_cannot_form_a_path_segment()
    {
        Account.Create(Guid.CreateVersion7(Now), "///", AccountKind.Expense, AccountRole.Category,
                       null, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.name_unusable");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_rejected(string name)
    {
        Account.Create(Guid.CreateVersion7(Now), name, AccountKind.Expense, AccountRole.Category,
                       null, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.name_required");
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_rejected()
    {
        Account.Create(Guid.CreateVersion7(Now), new string('x', Account.MaxNameLength + 1),
                       AccountKind.Expense, AccountRole.Category, null, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.name_too_long");
    }

    [Theory]
    [InlineData(AccountKind.Income, AccountRole.Category)]
    [InlineData(AccountKind.Expense, AccountRole.Category)]
    [InlineData(AccountKind.Asset, AccountRole.Bank)]
    [InlineData(AccountKind.Asset, AccountRole.Cash)]
    [InlineData(AccountKind.Asset, AccountRole.SavingsPocket)]
    [InlineData(AccountKind.Asset, AccountRole.Investment)]
    [InlineData(AccountKind.Equity, AccountRole.OpeningBalance)]
    [InlineData(AccountKind.Equity, AccountRole.Adjustment)]
    public void The_legal_kind_and_role_combinations_are_accepted(AccountKind kind, AccountRole role)
    {
        Account.Create(Guid.CreateVersion7(Now), "Whatever", kind, role, null, Currency.Eur, Now)
            .IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(AccountKind.Asset, AccountRole.Category)]
    [InlineData(AccountKind.Expense, AccountRole.Bank)]
    [InlineData(AccountKind.Income, AccountRole.SavingsPocket)]
    [InlineData(AccountKind.Asset, AccountRole.OpeningBalance)]
    [InlineData(AccountKind.Expense, AccountRole.Adjustment)]
    [InlineData(AccountKind.Liability, AccountRole.Bank)]
    public void An_illegal_kind_and_role_combination_is_rejected(AccountKind kind, AccountRole role)
    {
        Account.Create(Guid.CreateVersion7(Now), "Whatever", kind, role, null, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public void A_child_must_share_its_parent_s_kind()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        Account.Create(Guid.CreateVersion7(Now), "Salary", AccountKind.Income, AccountRole.Category,
                       gaming, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.parent_kind_mismatch");
    }

    [Fact]
    public void A_child_must_share_its_parent_s_currency()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        Account.Create(Guid.CreateVersion7(Now), "Steam", AccountKind.Expense, AccountRole.Category,
                       gaming, Currency.Usd, Now)
            .Error!.Code.Should().Be("account.currency_mismatch_with_parent");
    }

    [Fact]
    public void An_archived_parent_cannot_take_new_children()
    {
        var gaming = Root("Gaming", AccountKind.Expense, AccountRole.Category);
        gaming.Archive(Now);

        Account.Create(Guid.CreateVersion7(Now), "Steam", AccountKind.Expense, AccountRole.Category,
                       gaming, Currency.Eur, Now)
            .Error!.Code.Should().Be("account.parent_archived");
    }

    [Fact]
    public void A_new_account_records_when_it_was_created()
    {
        var account = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        account.CreatedAtUtc.Should().Be(Now);
        account.UpdatedAtUtc.Should().Be(Now);
        account.IsArchived.Should().BeFalse();
        account.IsCategory.Should().BeTrue();
    }

    [Fact]
    public void Archiving_is_idempotent_in_intent_but_reported_as_a_failure_when_repeated()
    {
        var account = Root("Gaming", AccountKind.Expense, AccountRole.Category);

        account.Archive(Now).IsSuccess.Should().BeTrue();
        account.IsArchived.Should().BeTrue();
        account.Archive(Now).Error!.Code.Should().Be("account.already_archived");
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter AccountCreationTests`
Expected: FAIL — `Money.Domain.Accounts` does not exist.

- [x] **Step 3: Implement the enums**

```csharp
namespace Money.Domain.Accounts;

public enum AccountKind
{
    Asset = 1,
    Liability = 2,
    Income = 3,
    Expense = 4,
    Equity = 5
}
```

```csharp
namespace Money.Domain.Accounts;

public enum AccountRole
{
    Bank = 1,
    Cash = 2,
    SavingsPocket = 3,
    Investment = 4,
    Category = 5,
    OpeningBalance = 6,
    Adjustment = 7
}
```

- [x] **Step 4: Implement `Account`**

```csharp
using System.Globalization;
using System.Text;
using Money.Domain.Money;
using Money.Domain.Primitives;

namespace Money.Domain.Accounts;

/// <summary>
/// A node in the single account tree. Categories are accounts too (Kind=Income|Expense,
/// Role=Category); that is what gives budgets and reports free hierarchy and subtree rollups.
/// The UI calls these "categories" and never says "account" about them.
/// </summary>
public sealed class Account
{
    public const int MaxNameLength = 120;

    // EF Core materialisation constructor. Never call this from domain code.
    private Account()
    {
        Name = null!;
        Path = null!;
        CurrencyCode = null!;
    }

    private Account(
        Guid id, string name, AccountKind kind, AccountRole role,
        Guid? parentAccountId, string path, string currencyCode, DateTimeOffset nowUtc)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Role = role;
        ParentAccountId = parentAccountId;
        Path = path;
        CurrencyCode = currencyCode;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public AccountKind Kind { get; private set; }
    public AccountRole Role { get; private set; }
    public Guid? ParentAccountId { get; private set; }

    /// <summary>Materialised path, e.g. "/expense/gaming/steam". Subtree queries are prefix matches.</summary>
    public string Path { get; private set; }

    public string CurrencyCode { get; private set; }
    public bool IsArchived { get; private set; }
    public DateOnly? OpenedOn { get; private set; }
    public int SortOrder { get; private set; }
    public string? ColorHex { get; private set; }
    public string? Icon { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public string ChildPathPrefix => Path + "/";

    public bool IsCategory => Role == AccountRole.Category;

    public static Result<Account> Create(
        Guid id, string name, AccountKind kind, AccountRole role,
        Account? parent, Currency currency, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var validatedName = ValidateName(name);
        if (validatedName.IsFailure) return validatedName.Error!;

        if (!IsLegalCombination(kind, role))
            return DomainErrors.Account.KindRoleMismatch(kind.ToString(), role.ToString());

        var slug = Slugify(validatedName.Value);
        if (slug.Length == 0) return DomainErrors.Account.NameUnusable(name);

        if (parent is not null)
        {
            if (parent.Kind != kind)
                return DomainErrors.Account.ParentKindMismatch(kind.ToString(), parent.Kind.ToString());
            if (parent.IsArchived) return DomainErrors.Account.ParentArchived(parent.Name);
            if (!string.Equals(parent.CurrencyCode, currency.Code, StringComparison.Ordinal))
                return DomainErrors.Account.CurrencyMismatchWithParent();
        }

        var path = parent is null ? RootPathFor(kind) + "/" + slug : parent.ChildPathPrefix + slug;

        return Result<Account>.Ok(new Account(
            id, validatedName.Value, kind, role, parent?.Id, path, currency.Code, nowUtc));
    }

    public Result Archive(DateTimeOffset nowUtc)
    {
        if (IsArchived) return DomainErrors.Account.AlreadyArchived(Name);

        IsArchived = true;
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    public Result Restore(DateTimeOffset nowUtc)
    {
        IsArchived = false;
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    public Result UpdatePresentation(
        int sortOrder, string? colorHex, string? icon, string? notes, DateOnly? openedOn,
        DateTimeOffset nowUtc)
    {
        SortOrder = sortOrder;
        ColorHex = colorHex;
        Icon = icon;
        Notes = notes;
        OpenedOn = openedOn;
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    internal void ApplyRename(string newName, string newPath, DateTimeOffset nowUtc)
    {
        Name = newName;
        Path = newPath;
        UpdatedAtUtc = nowUtc;
    }

    internal void ApplyMove(Guid? newParentId, string newPath, DateTimeOffset nowUtc)
    {
        ParentAccountId = newParentId;
        Path = newPath;
        UpdatedAtUtc = nowUtc;
    }

    internal void ApplyPath(string newPath, DateTimeOffset nowUtc)
    {
        Path = newPath;
        UpdatedAtUtc = nowUtc;
    }

    public static string RootPathFor(AccountKind kind) =>
        "/" + kind.ToString().ToLowerInvariant();

    /// <summary>Lower-cases, keeps letters and digits, and collapses everything else into single hyphens.</summary>
    public static string Slugify(string name)
    {
        var builder = new StringBuilder(name.Length);
        var pendingHyphen = false;

        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                pendingHyphen = false;
                builder.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return builder.ToString();
    }

    internal static Result<string> ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return DomainErrors.Account.NameRequired();
        if (trimmed.Length > MaxNameLength) return DomainErrors.Account.NameTooLong(MaxNameLength);
        return Result<string>.Ok(trimmed);
    }

    /// <summary>
    /// Public because it is a genuine domain question that callers outside this assembly ask:
    /// the category use case (Task 21) checks a pair before building anything.
    /// </summary>
    public static bool IsLegalCombination(AccountKind kind, AccountRole role) => role switch
    {
        AccountRole.Category => kind is AccountKind.Income or AccountKind.Expense,
        AccountRole.Bank or AccountRole.Cash or AccountRole.SavingsPocket or AccountRole.Investment
            => kind is AccountKind.Asset,
        AccountRole.OpeningBalance or AccountRole.Adjustment => kind is AccountKind.Equity,
        _ => false
    };

    public override string ToString() => $"{Name} ({Path})";
}
```

Note the `Étterem` slug test expects `étterem`, not `etterem`: `char.IsLetterOrDigit` accepts accented letters, and stripping diacritics would risk collapsing two distinct category names into one path.

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter AccountCreationTests`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/Money.Domain/Accounts tests/Money.Domain.Tests/Accounts
git commit -m "feat: add Account with kind/role constraints and a materialised path"
```

---

### Task 11: `AccountTree` — rename, reparent and subtree path recomputation

**Files:**
- Create: `src/Money.Domain/Accounts/AccountTree.cs`
- Test: `tests/Money.Domain.Tests/Accounts/AccountTreeTests.cs`

**Interfaces:**
- Consumes: `Account` (including its `internal` `ApplyRename` / `ApplyMove` / `ApplyPath`).
- Produces: `static class AccountTree` with
  - `static IReadOnlyList<Account> DescendantsOf(Account root, IEnumerable<Account> all)`
  - `static Result Rename(Account account, string newName, IReadOnlyCollection<Account> siblings, IReadOnlyCollection<Account> descendants, DateTimeOffset nowUtc)`
  - `static Result Move(Account account, Account? newParent, IReadOnlyCollection<Account> newSiblings, IReadOnlyCollection<Account> descendants, DateTimeOffset nowUtc)`
- Callers must supply the sibling set and the descendant set; the domain has no repository. Task 21's use case loads both.

- [x] **Step 1: Write the failing tests**

```csharp
using Money.Domain.Accounts;
using Money.Domain.Money;

namespace Money.Domain.Tests.Accounts;

public sealed class AccountTreeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private static Account New(string name, Account? parent = null,
                               AccountKind kind = AccountKind.Expense,
                               AccountRole role = AccountRole.Category) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, parent, Currency.Eur, Now).Value;

    [Fact]
    public void Descendants_are_everything_under_the_path_prefix_and_never_the_node_itself()
    {
        var gaming = New("Gaming");
        var steam = New("Steam", gaming);
        var deck = New("Deck", steam);
        var food = New("Food");

        var all = new[] { gaming, steam, deck, food };

        AccountTree.DescendantsOf(gaming, all).Should().BeEquivalentTo(new[] { steam, deck });
        AccountTree.DescendantsOf(food, all).Should().BeEmpty();
    }

    [Fact]
    public void A_prefix_that_only_looks_like_a_parent_is_not_a_descendant()
    {
        var gaming = New("Gaming");
        var gamingChairs = New("Gaming chairs");

        AccountTree.DescendantsOf(gaming, new[] { gaming, gamingChairs }).Should().BeEmpty();
    }

    [Fact]
    public void Renaming_rewrites_the_node_s_path_and_every_descendant_path()
    {
        var gaming = New("Gaming");
        var steam = New("Steam", gaming);
        var deck = New("Deck", steam);
        var descendants = new[] { steam, deck };

        var result = AccountTree.Rename(gaming, "Games", siblings: [], descendants, Later);

        result.IsSuccess.Should().BeTrue();
        gaming.Name.Should().Be("Games");
        gaming.Path.Should().Be("/expense/games");
        steam.Path.Should().Be("/expense/games/steam");
        deck.Path.Should().Be("/expense/games/steam/deck");
        deck.UpdatedAtUtc.Should().Be(Later);
    }

    [Fact]
    public void Renaming_to_a_name_a_sibling_already_uses_is_rejected()
    {
        var gaming = New("Gaming");
        var food = New("Food");

        AccountTree.Rename(gaming, "Food", siblings: [food], descendants: [], Later)
            .Error!.Code.Should().Be("account.duplicate_sibling_name");
        gaming.Name.Should().Be("Gaming");
    }

    [Fact]
    public void Renaming_to_a_name_that_only_differs_by_case_or_punctuation_is_rejected()
    {
        var gaming = New("Gaming");
        var eatingOut = New("Eating out");

        AccountTree.Rename(gaming, "eating-out", siblings: [eatingOut], descendants: [], Later)
            .Error!.Code.Should().Be("account.duplicate_sibling_name");
    }

    [Fact]
    public void Renaming_a_node_to_its_own_current_name_is_allowed()
    {
        var gaming = New("Gaming");

        AccountTree.Rename(gaming, "Gaming", siblings: [gaming], descendants: [], Later)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Moving_a_node_reparents_it_and_rewrites_the_whole_subtree()
    {
        var gaming = New("Gaming");
        var hobbies = New("Hobbies");
        var steam = New("Steam", gaming);
        var deck = New("Deck", steam);

        var result = AccountTree.Move(gaming, hobbies, newSiblings: [], descendants: [steam, deck], Later);

        result.IsSuccess.Should().BeTrue();
        gaming.ParentAccountId.Should().Be(hobbies.Id);
        gaming.Path.Should().Be("/expense/hobbies/gaming");
        steam.Path.Should().Be("/expense/hobbies/gaming/steam");
        deck.Path.Should().Be("/expense/hobbies/gaming/steam/deck");
    }

    [Fact]
    public void Moving_a_node_to_the_root_puts_it_back_under_its_kind()
    {
        var hobbies = New("Hobbies");
        var gaming = New("Gaming", hobbies);
        var steam = New("Steam", gaming);

        AccountTree.Move(gaming, newParent: null, newSiblings: [], descendants: [steam], Later)
            .IsSuccess.Should().BeTrue();

        gaming.ParentAccountId.Should().BeNull();
        gaming.Path.Should().Be("/expense/gaming");
        steam.Path.Should().Be("/expense/gaming/steam");
    }

    [Fact]
    public void A_node_cannot_be_moved_underneath_its_own_descendant()
    {
        var gaming = New("Gaming");
        var steam = New("Steam", gaming);

        AccountTree.Move(gaming, steam, newSiblings: [], descendants: [steam], Later)
            .Error!.Code.Should().Be("account.cannot_reparent_under_own_descendant");
        gaming.Path.Should().Be("/expense/gaming");
    }

    [Fact]
    public void A_node_cannot_be_moved_underneath_itself()
    {
        var gaming = New("Gaming");

        AccountTree.Move(gaming, gaming, newSiblings: [], descendants: [], Later)
            .Error!.Code.Should().Be("account.cannot_reparent_under_own_descendant");
    }

    [Fact]
    public void A_node_cannot_be_moved_under_a_parent_of_a_different_kind()
    {
        var expense = New("Gaming");
        var income = New("Salary", kind: AccountKind.Income);

        AccountTree.Move(expense, income, newSiblings: [], descendants: [], Later)
            .Error!.Code.Should().Be("account.parent_kind_mismatch");
    }

    [Fact]
    public void A_node_cannot_be_moved_under_an_archived_parent()
    {
        var gaming = New("Gaming");
        var hobbies = New("Hobbies");
        hobbies.Archive(Now);

        AccountTree.Move(gaming, hobbies, newSiblings: [], descendants: [], Later)
            .Error!.Code.Should().Be("account.parent_archived");
    }

    [Fact]
    public void A_move_that_would_collide_with_an_existing_sibling_is_rejected()
    {
        var gaming = New("Gaming");
        var hobbies = New("Hobbies");
        var existingGaming = New("Gaming", hobbies);

        AccountTree.Move(gaming, hobbies, newSiblings: [existingGaming], descendants: [], Later)
            .Error!.Code.Should().Be("account.duplicate_sibling_name");
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter AccountTreeTests`
Expected: FAIL — `AccountTree` does not exist.

- [x] **Step 3: Implement `AccountTree`**

```csharp
using Money.Domain.Primitives;

namespace Money.Domain.Accounts;

/// <summary>
/// Pure tree operations. Every mutation that changes a path rewrites the whole subtree in the
/// same call, so a materialised path can never go stale.
/// </summary>
public static class AccountTree
{
    public static IReadOnlyList<Account> DescendantsOf(Account root, IEnumerable<Account> all)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(all);

        var prefix = root.ChildPathPrefix;
        return all.Where(a => a.Path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    }

    public static Result Rename(
        Account account, string newName,
        IReadOnlyCollection<Account> siblings, IReadOnlyCollection<Account> descendants,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(account);

        var validated = Account.ValidateName(newName);
        if (validated.IsFailure) return Result.Fail(validated.Error!);

        var slug = Account.Slugify(validated.Value);
        if (slug.Length == 0) return DomainErrors.Account.NameUnusable(newName);

        var collision = FindCollision(slug, siblings, account.Id);
        if (collision is not null) return DomainErrors.Account.DuplicateSiblingName(validated.Value);

        var oldPath = account.Path;
        var newPath = ParentPrefixOf(oldPath) + slug;

        account.ApplyRename(validated.Value, newPath, nowUtc);
        RewriteDescendants(descendants, oldPath, newPath, nowUtc);

        return Result.Ok();
    }

    public static Result Move(
        Account account, Account? newParent,
        IReadOnlyCollection<Account> newSiblings, IReadOnlyCollection<Account> descendants,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (newParent is not null)
        {
            if (newParent.Id == account.Id
                || newParent.Path.StartsWith(account.ChildPathPrefix, StringComparison.Ordinal))
            {
                return DomainErrors.Account.CannotReparentUnderOwnDescendant();
            }

            if (newParent.Kind != account.Kind)
                return DomainErrors.Account.ParentKindMismatch(
                    account.Kind.ToString(), newParent.Kind.ToString());

            if (newParent.IsArchived) return DomainErrors.Account.ParentArchived(newParent.Name);

            if (!string.Equals(newParent.CurrencyCode, account.CurrencyCode, StringComparison.Ordinal))
                return DomainErrors.Account.CurrencyMismatchWithParent();
        }

        var slug = Account.Slugify(account.Name);
        if (FindCollision(slug, newSiblings, account.Id) is not null)
            return DomainErrors.Account.DuplicateSiblingName(account.Name);

        var oldPath = account.Path;
        var newPath = newParent is null
            ? Account.RootPathFor(account.Kind) + "/" + slug
            : newParent.ChildPathPrefix + slug;

        account.ApplyMove(newParent?.Id, newPath, nowUtc);
        RewriteDescendants(descendants, oldPath, newPath, nowUtc);

        return Result.Ok();
    }

    private static Account? FindCollision(
        string slug, IReadOnlyCollection<Account> siblings, Guid selfId) =>
        siblings.FirstOrDefault(s =>
            s.Id != selfId && string.Equals(Account.Slugify(s.Name), slug, StringComparison.Ordinal));

    private static string ParentPrefixOf(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return path[..(lastSlash + 1)];
    }

    private static void RewriteDescendants(
        IEnumerable<Account> descendants, string oldPath, string newPath, DateTimeOffset nowUtc)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return;

        foreach (var descendant in descendants)
        {
            descendant.ApplyPath(newPath + descendant.Path[oldPath.Length..], nowUtc);
        }
    }
}
```

`AccountTree` is in the same assembly as `Account`, so the `internal` `Apply*` methods are reachable without widening `Account`'s public surface.

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter AccountTreeTests`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/Money.Domain/Accounts tests/Money.Domain.Tests/Accounts
git commit -m "feat: add AccountTree with subtree-safe rename and reparent"
```

---

### Task 12: `Posting`, `PostingDraft` and `TransactionSourceKind`

**Files:**
- Create: `src/Money.Domain/Ledger/TransactionSourceKind.cs`, `PostingDraft.cs`, `Posting.cs`
- Test: `tests/Money.Domain.Tests/Ledger/PostingTests.cs`

**Interfaces:**
- Consumes: `Money`, `Currency`.
- Produces:
  - `enum TransactionSourceKind { Manual = 1, Recurring = 2, Accrual = 3, Import = 4 }` — `Recurring` is used by phase 3, `Accrual` by phase 6, `Import` by nothing in this plan. All four exist now so the idempotency index in Task 18 can be created once.
  - `sealed record PostingDraft(Guid AccountId, Money Amount, string? Memo = null)`
  - `sealed class Posting` with `Guid Id`, `Guid TransactionId`, `Guid AccountId`, `long AmountMinor`, `string CurrencyCode`, `string? Memo`, and `Money AmountIn(Currency currency)`

**Sign convention, restated where a reader will hit it:** positive is a debit. Asset and Expense go up with a positive amount; Income, Liability and Equity go up with a negative one.

- [x] **Step 1: Write the failing test**

```csharp
using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class PostingTests
{
    [Fact]
    public void A_draft_carries_an_account_an_amount_and_an_optional_memo()
    {
        var accountId = Guid.CreateVersion7();
        var draft = new PostingDraft(accountId, MoneyValue.Of(2000, Currency.Eur), "Dinner");

        draft.AccountId.Should().Be(accountId);
        draft.Amount.AmountMinor.Should().Be(2000);
        draft.Memo.Should().Be("Dinner");
    }

    [Fact]
    public void A_draft_s_memo_is_optional()
    {
        new PostingDraft(Guid.CreateVersion7(), MoneyValue.Of(1, Currency.Eur)).Memo.Should().BeNull();
    }

    [Fact]
    public void A_posting_can_be_read_back_as_money_in_a_known_currency()
    {
        var posting = Posting.CreateForTest(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            -6000, Currency.Eur.Code, memo: null);

        posting.AmountIn(Currency.Eur).Should().Be(MoneyValue.Of(-6000, Currency.Eur));
    }

    [Fact]
    public void Reading_a_posting_in_the_wrong_currency_is_a_programmer_error()
    {
        var posting = Posting.CreateForTest(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            100, Currency.Eur.Code, memo: null);

        var act = () => posting.AmountIn(Currency.Usd);

        act.Should().Throw<CurrencyMismatchException>();
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Money.Domain.Tests --filter PostingTests`
Expected: FAIL — `Money.Domain.Ledger` does not exist.

- [x] **Step 3: Implement the three types**

```csharp
namespace Money.Domain.Ledger;

/// <summary>
/// Where a transaction came from. Recurring is used in phase 3, Accrual in phase 6, Import never
/// in v1 - all four exist now so the idempotency unique index does not need a later migration.
/// </summary>
public enum TransactionSourceKind
{
    Manual = 1,
    Recurring = 2,
    Accrual = 3,
    Import = 4
}
```

```csharp
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Ledger;

/// <summary>One side of a transaction, as supplied by a caller. Positive is a debit.</summary>
public sealed record PostingDraft(Guid AccountId, MoneyValue Amount, string? Memo = null);
```

```csharp
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Ledger;

/// <summary>
/// One side of a persisted transaction. Positive is a debit: Asset and Expense accounts increase
/// with a positive amount, Income, Liability and Equity accounts with a negative one.
/// </summary>
public sealed class Posting
{
    // EF Core materialisation constructor.
    private Posting() => CurrencyCode = null!;

    internal Posting(
        Guid id, Guid transactionId, Guid accountId, long amountMinor, string currencyCode, string? memo)
    {
        Id = id;
        TransactionId = transactionId;
        AccountId = accountId;
        AmountMinor = amountMinor;
        CurrencyCode = currencyCode;
        Memo = memo;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public long AmountMinor { get; private set; }
    public string CurrencyCode { get; private set; }
    public string? Memo { get; private set; }

    public MoneyValue AmountIn(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (!string.Equals(currency.Code, CurrencyCode, StringComparison.Ordinal))
        {
            var actual = Currency.FromCode(CurrencyCode);
            throw new CurrencyMismatchException(
                actual.IsSuccess ? actual.Value : currency, currency);
        }

        return MoneyValue.Of(AmountMinor, currency);
    }

    /// <summary>Test seam. Production code only ever gets postings from Transaction.Create.</summary>
    internal static Posting CreateForTest(
        Guid id, Guid transactionId, Guid accountId, long amountMinor, string currencyCode, string? memo) =>
        new(id, transactionId, accountId, amountMinor, currencyCode, memo);
}
```

`CreateForTest` is `internal`; `InternalsVisibleTo("Money.Domain.Tests")` was added in Task 7.

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter PostingTests`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/Money.Domain/Ledger tests/Money.Domain.Tests/Ledger
git commit -m "feat: add Posting, PostingDraft and TransactionSourceKind"
```

---

### Task 13: The `Transaction` aggregate — I1, I2, I4

**Files:**
- Create: `src/Money.Domain/Ledger/Transaction.cs`
- Test: `tests/Money.Domain.Tests/Ledger/TransactionCreationTests.cs`

**Interfaces:**
- Consumes: `Account`, `PostingDraft`, `Posting`, `Money`, `Currency`, `DomainErrors.Transaction`.
- Produces: `sealed class Transaction` with
  - `Guid Id`, `DateOnly OccurredOn`, `DateTimeOffset BookedAtUtc`, `string Description`, `string? Payee`, `TransactionSourceKind SourceKind`, `Guid? SourceId`, `string? ExternalRef`, `bool IsVoided`, `DateTimeOffset? VoidedAtUtc`, `string? VoidReason`, `DateTimeOffset CreatedAtUtc`, `DateTimeOffset UpdatedAtUtc`, `IReadOnlyList<Posting> Postings`
  - `static Result<Transaction> Create(Guid id, DateOnly occurredOn, string description, string? payee, TransactionSourceKind sourceKind, Guid? sourceId, IReadOnlyList<PostingDraft> postings, IReadOnlyDictionary<Guid, Account> accountsById, DateTimeOffset nowUtc)`
- Later tasks add `Replace` and `Void` (Task 14).

This is the invariant boundary. **I1** (postings sum to zero per currency) and **I2** (at least two postings) are enforced here; **I4** (no postings on archived accounts) too. There is no other constructor, so an unbalanced transaction is unrepresentable.

- [x] **Step 1: Write the failing tests**

```csharp
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class TransactionCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static Account NewAccount(string name, AccountKind kind, AccountRole role) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, null, Currency.Eur, Now).Value;

    private static IReadOnlyDictionary<Guid, Account> Lookup(params Account[] accounts) =>
        accounts.ToDictionary(a => a.Id);

    private static MoneyValue Eur(long minor) => MoneyValue.Of(minor, Currency.Eur);

    [Fact]
    public void A_balanced_two_sided_transaction_is_created()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", "Trattoria",
            TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now);

        result.IsSuccess.Should().BeTrue();
        var transaction = result.Value;
        transaction.Postings.Should().HaveCount(2);
        transaction.Postings.Sum(p => p.AmountMinor).Should().Be(0);
        transaction.Description.Should().Be("Dinner");
        transaction.Payee.Should().Be("Trattoria");
        transaction.BookedAtUtc.Should().Be(Now);
        transaction.CreatedAtUtc.Should().Be(Now);
        transaction.IsVoided.Should().BeFalse();
        transaction.SourceKind.Should().Be(TransactionSourceKind.Manual);
    }

    [Fact]
    public void A_split_transaction_with_three_sides_is_created()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var groceries = NewAccount("Groceries", AccountKind.Expense, AccountRole.Category);
        var alcohol = NewAccount("Alcohol", AccountKind.Expense, AccountRole.Category);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Shop", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(groceries.Id, Eur(5000)),
                new PostingDraft(alcohol.Id, Eur(1000)),
                new PostingDraft(bank.Id, Eur(-6000))
            ],
            Lookup(bank, groceries, alcohol), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Postings.Should().HaveCount(3);
    }

    [Fact]
    public void An_unbalanced_transaction_cannot_be_created()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-1900))],
            Lookup(bank, food), Now);

        result.Error!.Code.Should().Be("transaction.does_not_balance");
        result.Error.Message.Should().Contain("100");
    }

    [Fact]
    public void A_transaction_with_one_side_cannot_be_created()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Mystery", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(bank.Id, Eur(0))], Lookup(bank), Now);

        result.Error!.Code.Should().Be("transaction.too_few_entries");
    }

    [Fact]
    public void A_transaction_with_no_sides_cannot_be_created()
    {
        Transaction.Create(Guid.CreateVersion7(Now), Today, "Mystery", null,
                           TransactionSourceKind.Manual, null, [], Lookup(), Now)
            .Error!.Code.Should().Be("transaction.too_few_entries");
    }

    [Fact]
    public void A_zero_amount_entry_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Nothing", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(0)), new PostingDraft(bank.Id, Eur(0))],
            Lookup(bank, food), Now)
            .Error!.Code.Should().Be("transaction.zero_amount");
    }

    [Fact]
    public void An_entry_on_an_archived_account_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
        food.Archive(Now);

        var result = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now);

        result.Error!.Code.Should().Be("transaction.account_archived");
        result.Error.Message.Should().Contain("Food");
    }

    [Fact]
    public void An_entry_on_an_unknown_account_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var ghost = Guid.CreateVersion7(Now);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(ghost, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank), Now)
            .Error!.Code.Should().Be("transaction.account_unknown");
    }

    [Fact]
    public void An_entry_whose_currency_differs_from_its_account_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(food.Id, MoneyValue.Of(2000, Currency.Usd)),
                new PostingDraft(bank.Id, MoneyValue.Of(-2000, Currency.Usd))
            ],
            Lookup(bank, food), Now)
            .Error!.Code.Should().Be("transaction.currency_mismatch_with_account");
    }

    [Fact]
    public void The_same_account_cannot_appear_twice()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [
                new PostingDraft(food.Id, Eur(1000)),
                new PostingDraft(food.Id, Eur(1000)),
                new PostingDraft(bank.Id, Eur(-2000))
            ],
            Lookup(bank, food), Now)
            .Error!.Code.Should().Be("transaction.duplicate_account");
    }

    [Fact]
    public void A_blank_description_is_rejected()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        Transaction.Create(
            Guid.CreateVersion7(Now), Today, "   ", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now)
            .Error!.Code.Should().Be("transaction.description_required");
    }

    [Fact]
    public void Every_posting_gets_the_transaction_s_id_and_a_unique_id_of_its_own()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
        var transactionId = Guid.CreateVersion7(Now);

        var transaction = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now).Value;

        transaction.Postings.Should().OnlyContain(p => p.TransactionId == transaction.Id);
        transaction.Postings.Select(p => p.Id).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void The_postings_collection_is_not_mutable_from_outside()
    {
        var bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
        var food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);

        var transaction = Transaction.Create(
            Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
            [new PostingDraft(food.Id, Eur(2000)), new PostingDraft(bank.Id, Eur(-2000))],
            Lookup(bank, food), Now).Value;

        transaction.Postings.Should().BeAssignableTo<IReadOnlyList<Posting>>();
        (transaction.Postings as ICollection<Posting>)?.IsReadOnly.Should().NotBe(false);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter TransactionCreationTests`
Expected: FAIL — `Transaction` does not exist.

- [x] **Step 3: Implement `Transaction`**

```csharp
using System.Collections.ObjectModel;
using Money.Domain.Accounts;
using Money.Domain.Primitives;

namespace Money.Domain.Ledger;

/// <summary>
/// The invariant boundary of the ledger. There is no other way to build a transaction, so
/// an unbalanced one (I1) or a one-sided one (I2) is unrepresentable, and entries against an
/// archived account (I4) cannot be created.
/// </summary>
public sealed class Transaction
{
    private readonly List<Posting> _postings = [];

    // EF Core materialisation constructor.
    private Transaction() => Description = null!;

    private Transaction(
        Guid id, DateOnly occurredOn, string description, string? payee,
        TransactionSourceKind sourceKind, Guid? sourceId, DateTimeOffset nowUtc)
    {
        Id = id;
        OccurredOn = occurredOn;
        Description = description;
        Payee = payee;
        SourceKind = sourceKind;
        SourceId = sourceId;
        BookedAtUtc = nowUtc;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public DateTimeOffset BookedAtUtc { get; private set; }
    public string Description { get; private set; }
    public string? Payee { get; private set; }
    public TransactionSourceKind SourceKind { get; private set; }
    public Guid? SourceId { get; private set; }
    public string? ExternalRef { get; private set; }
    public bool IsVoided { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<Posting> Postings => new ReadOnlyCollection<Posting>(_postings);

    public static Result<Transaction> Create(
        Guid id, DateOnly occurredOn, string description, string? payee,
        TransactionSourceKind sourceKind, Guid? sourceId,
        IReadOnlyList<PostingDraft> postings,
        IReadOnlyDictionary<Guid, Account> accountsById,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(postings);
        ArgumentNullException.ThrowIfNull(accountsById);

        var trimmedDescription = description?.Trim();
        if (string.IsNullOrEmpty(trimmedDescription))
            return DomainErrors.Transaction.DescriptionRequired();

        var transaction = new Transaction(
            id, occurredOn, trimmedDescription, payee?.Trim(), sourceKind, sourceId, nowUtc);

        var built = BuildPostings(id, postings, accountsById, nowUtc);
        if (built.IsFailure) return built.Error!;

        transaction._postings.AddRange(built.Value);
        return Result<Transaction>.Ok(transaction);
    }

    internal static Result<List<Posting>> BuildPostings(
        Guid transactionId,
        IReadOnlyList<PostingDraft> drafts,
        IReadOnlyDictionary<Guid, Account> accountsById,
        DateTimeOffset nowUtc)
    {
        if (drafts.Count < 2) return DomainErrors.Transaction.TooFewPostings(drafts.Count);

        var seen = new HashSet<Guid>();
        var postings = new List<Posting>(drafts.Count);

        foreach (var draft in drafts)
        {
            if (!accountsById.TryGetValue(draft.AccountId, out var account))
                return DomainErrors.Transaction.AccountUnknown(draft.AccountId);

            if (!seen.Add(draft.AccountId))
                return DomainErrors.Transaction.DuplicateAccount(account.Name);

            if (account.IsArchived) return DomainErrors.Transaction.AccountArchived(account.Name);

            if (draft.Amount.IsZero) return DomainErrors.Transaction.ZeroAmount();

            if (!string.Equals(draft.Amount.Currency.Code, account.CurrencyCode, StringComparison.Ordinal))
                return DomainErrors.Transaction.CurrencyMismatchWithAccount(account.Name);

            postings.Add(new Posting(
                Guid.CreateVersion7(nowUtc), transactionId, draft.AccountId,
                draft.Amount.AmountMinor, draft.Amount.Currency.Code, draft.Memo?.Trim()));
        }

        foreach (var group in postings.GroupBy(p => p.CurrencyCode, StringComparer.Ordinal))
        {
            var residual = group.Sum(p => p.AmountMinor);
            if (residual != 0) return DomainErrors.Transaction.DoesNotBalance(group.Key, residual);
        }

        return Result<List<Posting>>.Ok(postings);
    }

    public override string ToString() =>
        $"{OccurredOn:O} {Description} ({_postings.Count} entries){(IsVoided ? " [voided]" : "")}";
}
```

Ordering note: the count check sits inside `BuildPostings` while the description check sits in `Create`. That is deliberate — `Replace` (Task 14) reuses `BuildPostings` and needs the same count rule, but re-validates its own description.

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter TransactionCreationTests`
Expected: PASS.

- [x] **Step 5: Run the architecture tests**

Run: `dotnet test tests/Money.Architecture.Tests`
Expected: PASS — `Postings` returns `ReadOnlyCollection<Posting>` typed as `IReadOnlyList<Posting>`, which the mutable-collection rule allows. If you exposed `_postings` directly, that test fails, which is the point.

- [x] **Step 6: Commit**

```bash
git add src/Money.Domain/Ledger tests/Money.Domain.Tests/Ledger
git commit -m "feat: add Transaction aggregate enforcing the balancing invariants I1, I2 and I4"
```

---

### Task 14: Voiding and replacing a transaction — I11

**Files:**
- Modify: `src/Money.Domain/Ledger/Transaction.cs`
- Test: `tests/Money.Domain.Tests/Ledger/TransactionVoidAndReplaceTests.cs`

**Interfaces:**
- Produces on `Transaction`:
  - `Result Void(string reason, DateTimeOffset nowUtc)`
  - `Result Replace(DateOnly occurredOn, string description, string? payee, IReadOnlyList<PostingDraft> postings, IReadOnlyDictionary<Guid, Account> accountsById, DateTimeOffset nowUtc)`
  - `Result SetExternalRef(string? externalRef, DateTimeOffset nowUtc)`

Spec D11: transactions are voided, never deleted. **I11** — a voided transaction contributes to no balance, report or budget — is enforced by every reader filtering `IsVoided`, and is property-tested in Task 15.

- [x] **Step 1: Write the failing tests**

```csharp
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class TransactionVoidAndReplaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddDays(1);
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static Account NewAccount(string name, AccountKind kind, AccountRole role) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, null, Currency.Eur, Now).Value;

    private static MoneyValue Eur(long minor) => MoneyValue.Of(minor, Currency.Eur);

    private readonly Account _bank = NewAccount("Current", AccountKind.Asset, AccountRole.Bank);
    private readonly Account _food = NewAccount("Food", AccountKind.Expense, AccountRole.Category);
    private readonly Account _fun = NewAccount("Fun", AccountKind.Expense, AccountRole.Category);

    private IReadOnlyDictionary<Guid, Account> Accounts => new[] { _bank, _food, _fun }.ToDictionary(a => a.Id);

    private Transaction ADinner() => Transaction.Create(
        Guid.CreateVersion7(Now), Today, "Dinner", null, TransactionSourceKind.Manual, null,
        [new PostingDraft(_food.Id, Eur(2000)), new PostingDraft(_bank.Id, Eur(-2000))],
        Accounts, Now).Value;

    [Fact]
    public void Voiding_records_when_and_why_and_keeps_the_entries()
    {
        var transaction = ADinner();

        var result = transaction.Void("Entered twice", Later);

        result.IsSuccess.Should().BeTrue();
        transaction.IsVoided.Should().BeTrue();
        transaction.VoidedAtUtc.Should().Be(Later);
        transaction.VoidReason.Should().Be("Entered twice");
        transaction.UpdatedAtUtc.Should().Be(Later);
        transaction.Postings.Should().HaveCount(2, "voiding is not deleting");
    }

    [Fact]
    public void Voiding_twice_is_rejected()
    {
        var transaction = ADinner();
        transaction.Void("Entered twice", Later);

        transaction.Void("Again", Later).Error!.Code.Should().Be("transaction.already_voided");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Voiding_without_a_reason_is_rejected(string reason)
    {
        ADinner().Void(reason, Later).Error!.Code.Should().Be("transaction.void_reason_required");
    }

    [Fact]
    public void Replacing_swaps_the_entries_and_keeps_the_identity()
    {
        var transaction = ADinner();
        var originalId = transaction.Id;

        var result = transaction.Replace(
            new DateOnly(2026, 9, 2), "Cinema", "Cinema City",
            [new PostingDraft(_fun.Id, Eur(1500)), new PostingDraft(_bank.Id, Eur(-1500))],
            Accounts, Later);

        result.IsSuccess.Should().BeTrue();
        transaction.Id.Should().Be(originalId);
        transaction.OccurredOn.Should().Be(new DateOnly(2026, 9, 2));
        transaction.Description.Should().Be("Cinema");
        transaction.Payee.Should().Be("Cinema City");
        transaction.UpdatedAtUtc.Should().Be(Later);
        transaction.CreatedAtUtc.Should().Be(Now, "creation time never changes");
        transaction.Postings.Should().HaveCount(2);
        transaction.Postings.Should().Contain(p => p.AccountId == _fun.Id && p.AmountMinor == 1500);
        transaction.Postings.Should().NotContain(p => p.AccountId == _food.Id);
    }

    [Fact]
    public void A_replacement_that_does_not_balance_leaves_the_original_untouched()
    {
        var transaction = ADinner();

        var result = transaction.Replace(
            Today, "Broken",
            null,
            [new PostingDraft(_fun.Id, Eur(1500)), new PostingDraft(_bank.Id, Eur(-1400))],
            Accounts, Later);

        result.Error!.Code.Should().Be("transaction.does_not_balance");
        transaction.Description.Should().Be("Dinner");
        transaction.Postings.Should().Contain(p => p.AccountId == _food.Id);
        transaction.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void A_voided_transaction_cannot_be_edited()
    {
        var transaction = ADinner();
        transaction.Void("Mistake", Later);

        transaction.Replace(
            Today, "Cinema", null,
            [new PostingDraft(_fun.Id, Eur(1500)), new PostingDraft(_bank.Id, Eur(-1500))],
            Accounts, Later)
            .Error!.Code.Should().Be("transaction.cannot_edit_voided");
    }

    [Fact]
    public void An_external_reference_can_be_attached_and_cleared()
    {
        var transaction = ADinner();

        transaction.SetExternalRef("receipt-1234", Later).IsSuccess.Should().BeTrue();
        transaction.ExternalRef.Should().Be("receipt-1234");

        transaction.SetExternalRef(null, Later);
        transaction.ExternalRef.Should().BeNull();
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter TransactionVoidAndReplaceTests`
Expected: FAIL — `Void` and `Replace` do not exist.

- [x] **Step 3: Add the three methods to `Transaction`**

Insert after `BuildPostings`:

```csharp
    public Result Void(string reason, DateTimeOffset nowUtc)
    {
        if (IsVoided) return DomainErrors.Transaction.AlreadyVoided();

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return DomainErrors.Transaction.VoidReasonRequired();

        IsVoided = true;
        VoidedAtUtc = nowUtc;
        VoidReason = trimmed;
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }

    /// <summary>
    /// Replaces the whole content of the transaction. Either every change applies or none does:
    /// the new entries are built and validated before anything is mutated.
    /// </summary>
    public Result Replace(
        DateOnly occurredOn, string description, string? payee,
        IReadOnlyList<PostingDraft> postings,
        IReadOnlyDictionary<Guid, Account> accountsById,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(postings);
        ArgumentNullException.ThrowIfNull(accountsById);

        if (IsVoided) return DomainErrors.Transaction.CannotEditVoided();

        var trimmedDescription = description?.Trim();
        if (string.IsNullOrEmpty(trimmedDescription))
            return DomainErrors.Transaction.DescriptionRequired();

        var built = BuildPostings(Id, postings, accountsById, nowUtc);
        if (built.IsFailure) return Result.Fail(built.Error!);

        OccurredOn = occurredOn;
        Description = trimmedDescription;
        Payee = payee?.Trim();
        _postings.Clear();
        _postings.AddRange(built.Value);
        UpdatedAtUtc = nowUtc;

        return Result.Ok();
    }

    public Result SetExternalRef(string? externalRef, DateTimeOffset nowUtc)
    {
        ExternalRef = string.IsNullOrWhiteSpace(externalRef) ? null : externalRef.Trim();
        UpdatedAtUtc = nowUtc;
        return Result.Ok();
    }
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter TransactionVoidAndReplaceTests`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/Money.Domain/Ledger tests/Money.Domain.Tests/Ledger
git commit -m "feat: add transaction void and replace, keeping history instead of deleting it"
```

---

### Task 15: `BalanceCalculator` and the I3 / I11 property tests

**Files:**
- Create: `src/Money.Domain/Ledger/BalanceCalculator.cs`
- Create: `tests/Money.TestSupport/LedgerGen.cs`
- Test: `tests/Money.Domain.Tests/Ledger/BalanceCalculatorTests.cs`, `LedgerBalancePropertyTests.cs`

**Interfaces:**
- Consumes: `Account`, `Transaction`, `Posting`, `Money`, `Currency`, `DateRange`.
- Produces: `static class BalanceCalculator` with
  - `static bool IsInSubtree(Account candidate, Account root)`
  - `static Money BalanceOf(Guid accountId, Currency currency, IEnumerable<Transaction> transactions, DateOnly? asOfInclusive = null)`
  - `static Money SubtreeBalance(Account root, Currency currency, IReadOnlyDictionary<Guid, Account> accountsById, IEnumerable<Transaction> transactions, DateRange? window = null)`
  - `static Money TotalSpending(Currency currency, IReadOnlyDictionary<Guid, Account> accountsById, IEnumerable<Transaction> transactions, DateRange window)`
- Produces in `Money.TestSupport`: `static class LedgerGen` with `sealed record GeneratedLedger(IReadOnlyList<Account> Accounts, IReadOnlyList<Transaction> Transactions, IReadOnlyDictionary<Guid, Account> AccountsById, Account Bank, Account Savings, Account Investment, Account Salary, Account Groceries, Account Fun, Account OpeningEquity)` and `static CsCheck.Gen<GeneratedLedger> Ledgers`.

This is the **reference implementation**. Task 20's SQL must agree with it; the golden dashboard test in phase 7 is checked against it. Balances are never stored (spec 5.3).

- [x] **Step 1: Write the failing example tests**

```csharp
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Periods;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class BalanceCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly Account _bank = Account.Create(
        Guid.CreateVersion7(Now), "Current", AccountKind.Asset, AccountRole.Bank, null, Currency.Eur, Now).Value;

    private readonly Account _gaming = Account.Create(
        Guid.CreateVersion7(Now), "Gaming", AccountKind.Expense, AccountRole.Category, null, Currency.Eur, Now).Value;

    private Account _steam = null!;

    private IReadOnlyDictionary<Guid, Account> Accounts =>
        new[] { _bank, _gaming, _steam }.ToDictionary(a => a.Id);

    private static MoneyValue Eur(long minor) => MoneyValue.Of(minor, Currency.Eur);

    public BalanceCalculatorTests()
    {
        _steam = Account.Create(Guid.CreateVersion7(Now), "Steam", AccountKind.Expense,
                                AccountRole.Category, _gaming, Currency.Eur, Now).Value;
    }

    private Transaction Spend(Account category, long minor, DateOnly on) =>
        Transaction.Create(Guid.CreateVersion7(Now), on, "Spend", null,
                           TransactionSourceKind.Manual, null,
                           [new PostingDraft(category.Id, Eur(minor)), new PostingDraft(_bank.Id, Eur(-minor))],
                           Accounts, Now).Value;

    [Fact]
    public void An_account_balance_is_the_sum_of_its_entries()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 9, 1)),
            Spend(_steam, 2500, new DateOnly(2026, 9, 2))
        };

        BalanceCalculator.BalanceOf(_bank.Id, Currency.Eur, transactions)
            .Should().Be(Eur(-3500));
        BalanceCalculator.BalanceOf(_gaming.Id, Currency.Eur, transactions)
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void A_balance_as_of_a_date_ignores_later_entries()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 9, 1)),
            Spend(_gaming, 2000, new DateOnly(2026, 9, 5))
        };

        BalanceCalculator.BalanceOf(_gaming.Id, Currency.Eur, transactions,
                                    asOfInclusive: new DateOnly(2026, 9, 3))
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void A_voided_transaction_contributes_nothing()
    {
        var kept = Spend(_gaming, 1000, new DateOnly(2026, 9, 1));
        var voided = Spend(_gaming, 9999, new DateOnly(2026, 9, 2));
        voided.Void("Mistake", Now);

        BalanceCalculator.BalanceOf(_gaming.Id, Currency.Eur, new[] { kept, voided })
            .Should().Be(Eur(1000));
    }

    [Fact]
    public void A_subtree_balance_rolls_up_children()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 9, 1)),
            Spend(_steam, 2500, new DateOnly(2026, 9, 2))
        };

        BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, Accounts, transactions)
            .Should().Be(Eur(3500));
        BalanceCalculator.SubtreeBalance(_steam, Currency.Eur, Accounts, transactions)
            .Should().Be(Eur(2500));
    }

    [Fact]
    public void A_subtree_balance_can_be_windowed_to_a_period()
    {
        var transactions = new[]
        {
            Spend(_gaming, 1000, new DateOnly(2026, 8, 31)),
            Spend(_gaming, 2000, new DateOnly(2026, 9, 15)),
            Spend(_steam, 500, new DateOnly(2026, 10, 1))
        };
        var september = DateRange.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)).Value;

        BalanceCalculator.SubtreeBalance(_gaming, Currency.Eur, Accounts, transactions, september)
            .Should().Be(Eur(2000));
    }

    [Fact]
    public void An_account_is_in_its_own_subtree_but_a_lookalike_sibling_is_not()
    {
        var gamingChairs = Account.Create(Guid.CreateVersion7(Now), "Gaming chairs", AccountKind.Expense,
                                          AccountRole.Category, null, Currency.Eur, Now).Value;

        BalanceCalculator.IsInSubtree(_gaming, _gaming).Should().BeTrue();
        BalanceCalculator.IsInSubtree(_steam, _gaming).Should().BeTrue();
        BalanceCalculator.IsInSubtree(gamingChairs, _gaming).Should().BeFalse();
    }
}
```

- [x] **Step 2: Write the ledger generator in `Money.TestSupport`**

This generator is used by the I3 property test here, the I12 property test in Task 16, and again in phases 4–7. Build it once, properly.

```csharp
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
        var eur = Currency.Eur;
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
```

`Money.TestSupport` now needs the `CsCheck` package reference.

- [x] **Step 3: Write the failing I3 / I11 property tests**

```csharp
using CsCheck;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.TestSupport;

namespace Money.Domain.Tests.Ledger;

/// <summary>
/// Invariant I1: postings sum to zero. I3: an account's balance is the sum of its non-voided
/// postings, and equals the balance recomputed from scratch. I11: a voided transaction
/// contributes to no balance.
/// </summary>
public sealed class LedgerBalancePropertyTests
{
    [Fact]
    public void Every_generated_transaction_balances_to_zero()
    {
        LedgerGen.Ledgers.Sample(ledger =>
            ledger.Transactions.All(t => t.Postings.Sum(p => p.AmountMinor) == 0),
            iter: 2_000);
    }

    [Fact]
    public void Every_generated_transaction_has_at_least_two_entries()
    {
        LedgerGen.Ledgers.Sample(ledger =>
            ledger.Transactions.All(t => t.Postings.Count >= 2), iter: 2_000);
    }

    [Fact]
    public void An_account_balance_equals_the_sum_of_its_non_voided_entries()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            foreach (var account in ledger.Accounts)
            {
                var expected = ledger.Transactions
                    .Where(t => !t.IsVoided)
                    .SelectMany(t => t.Postings)
                    .Where(p => p.AccountId == account.Id)
                    .Sum(p => p.AmountMinor);

                var actual = BalanceCalculator
                    .BalanceOf(account.Id, Currency.Eur, ledger.Transactions).AmountMinor;

                if (actual != expected) return false;
            }

            return true;
        }, iter: 2_000);
    }

    [Fact]
    public void All_account_balances_together_sum_to_zero()
    {
        // The books balance: this is the whole-ledger form of I1.
        LedgerGen.Ledgers.Sample(ledger =>
            ledger.Accounts.Sum(a =>
                BalanceCalculator.BalanceOf(a.Id, Currency.Eur, ledger.Transactions).AmountMinor) == 0,
            iter: 2_000);
    }

    [Fact]
    public void Voiding_a_transaction_removes_exactly_its_own_contribution()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var live = ledger.Transactions.Where(t => !t.IsVoided).ToList();
            if (live.Count == 0) return true;

            var target = live[0];
            var before = BalanceCalculator.BalanceOf(ledger.Bank.Id, Currency.Eur, ledger.Transactions);
            var contribution = target.Postings.Where(p => p.AccountId == ledger.Bank.Id)
                                              .Sum(p => p.AmountMinor);

            target.Void("Property test", new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

            var after = BalanceCalculator.BalanceOf(ledger.Bank.Id, Currency.Eur, ledger.Transactions);

            return after.AmountMinor == before.AmountMinor - contribution;
        }, iter: 2_000);
    }
}
```

- [x] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter Balance`
Expected: FAIL — `BalanceCalculator` does not exist.

- [x] **Step 5: Implement `BalanceCalculator`**

```csharp
using Money.Domain.Accounts;
using Money.Domain.Money;
using Money.Domain.Periods;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Ledger;

/// <summary>
/// The reference implementation of every balance question. Balances are never stored (spec 5.3);
/// the SQL in Money.Infrastructure must agree with this class, and is tested against it.
/// </summary>
public static class BalanceCalculator
{
    public static bool IsInSubtree(Account candidate, Account root)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(root);

        return string.Equals(candidate.Path, root.Path, StringComparison.Ordinal)
               || candidate.Path.StartsWith(root.ChildPathPrefix, StringComparison.Ordinal);
    }

    public static MoneyValue BalanceOf(
        Guid accountId, Currency currency,
        IEnumerable<Transaction> transactions, DateOnly? asOfInclusive = null)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(transactions);

        var total = 0L;

        foreach (var transaction in transactions)
        {
            if (transaction.IsVoided) continue;
            if (asOfInclusive is { } asOf && transaction.OccurredOn > asOf) continue;

            foreach (var posting in transaction.Postings)
            {
                if (posting.AccountId != accountId) continue;
                if (!string.Equals(posting.CurrencyCode, currency.Code, StringComparison.Ordinal)) continue;
                total = checked(total + posting.AmountMinor);
            }
        }

        return MoneyValue.Of(total, currency);
    }

    public static MoneyValue SubtreeBalance(
        Account root, Currency currency,
        IReadOnlyDictionary<Guid, Account> accountsById,
        IEnumerable<Transaction> transactions,
        DateRange? window = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(accountsById);
        ArgumentNullException.ThrowIfNull(transactions);

        var total = 0L;

        foreach (var transaction in transactions)
        {
            if (transaction.IsVoided) continue;
            if (window is { } range && !range.Contains(transaction.OccurredOn)) continue;

            foreach (var posting in transaction.Postings)
            {
                if (!string.Equals(posting.CurrencyCode, currency.Code, StringComparison.Ordinal)) continue;
                if (!accountsById.TryGetValue(posting.AccountId, out var account)) continue;
                if (!IsInSubtree(account, root)) continue;
                total = checked(total + posting.AmountMinor);
            }
        }

        return MoneyValue.Of(total, currency);
    }

    /// <summary>
    /// Total spending in a window: postings to Kind=Expense accounts only. Transfers, pocket
    /// funding and investment purchases touch no expense account, so they are structurally
    /// excluded rather than filtered out by a rule someone could forget (I12).
    /// </summary>
    public static MoneyValue TotalSpending(
        Currency currency,
        IReadOnlyDictionary<Guid, Account> accountsById,
        IEnumerable<Transaction> transactions,
        DateRange window)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(accountsById);
        ArgumentNullException.ThrowIfNull(transactions);

        var total = 0L;

        foreach (var transaction in transactions)
        {
            if (transaction.IsVoided) continue;
            if (!window.Contains(transaction.OccurredOn)) continue;

            foreach (var posting in transaction.Postings)
            {
                if (!string.Equals(posting.CurrencyCode, currency.Code, StringComparison.Ordinal)) continue;
                if (!accountsById.TryGetValue(posting.AccountId, out var account)) continue;
                if (account.Kind != AccountKind.Expense) continue;
                total = checked(total + posting.AmountMinor);
            }
        }

        return MoneyValue.Of(total, currency);
    }
}
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests --filter Balance`
Expected: PASS — five properties at 2,000 iterations each plus six example tests.

- [x] **Step 7: Commit**

```bash
git add src/Money.Domain/Ledger tests/Money.TestSupport tests/Money.Domain.Tests/Ledger
git commit -m "feat: add BalanceCalculator with I1, I3 and I11 property tests"
```

---

### Task 16: `LedgerTemplates` and the I12 spending-exclusion property test

**Files:**
- Create: `src/Money.Domain/Ledger/LedgerTemplates.cs`
- Test: `tests/Money.Domain.Tests/Ledger/LedgerTemplatesTests.cs`, `SpendingExclusionPropertyTests.cs`

**Interfaces:**
- Consumes: `Transaction`, `Account`, `Money`, `BalanceCalculator`.
- Produces: `static class LedgerTemplates` with
  - `static Result<Transaction> Expense(Guid id, DateOnly occurredOn, string description, string? payee, Account paidFrom, Account category, Money amount, DateTimeOffset nowUtc)`
  - `static Result<Transaction> Income(Guid id, DateOnly occurredOn, string description, string? payee, Account receivedInto, Account category, Money amount, DateTimeOffset nowUtc)`
  - `static Result<Transaction> Transfer(Guid id, DateOnly occurredOn, string description, Account from, Account to, Money amount, DateTimeOffset nowUtc)`
  - `static Result<Transaction> OpeningBalance(Guid id, DateOnly occurredOn, Account account, Account openingBalanceEquity, Money amount, DateTimeOffset nowUtc)`
- These back the sugar endpoints `POST /transactions/quick-expense` and `POST /transactions/transfer` (Task 23), the first-run wizard (Task 24), and pocket funding in phase 5.

**I12** is the spec's headline promise: transfers, pocket funding and investment purchases can never be counted as spending. It holds structurally — those templates touch no `Kind=Expense` account — so the property test's job is to prove no template quietly breaks it.

- [x] **Step 1: Write the failing example tests**

```csharp
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Domain.Tests.Ledger;

public sealed class LedgerTemplatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static Account Root(string name, AccountKind kind, AccountRole role) =>
        Account.Create(Guid.CreateVersion7(Now), name, kind, role, null, Currency.Eur, Now).Value;

    private static MoneyValue Eur(long minor) => MoneyValue.Of(minor, Currency.Eur);

    private readonly Account _bank = Root("Current", AccountKind.Asset, AccountRole.Bank);
    private readonly Account _savings = Root("Rainy day", AccountKind.Asset, AccountRole.SavingsPocket);
    private readonly Account _food = Root("Food", AccountKind.Expense, AccountRole.Category);
    private readonly Account _salary = Root("Salary", AccountKind.Income, AccountRole.Category);
    private readonly Account _opening = Root("Opening balance", AccountKind.Equity, AccountRole.OpeningBalance);

    [Fact]
    public void An_expense_debits_the_category_and_credits_the_account()
    {
        var transaction = LedgerTemplates.Expense(
            Guid.CreateVersion7(Now), Today, "Dinner", "Trattoria",
            _bank, _food, Eur(2000), Now).Value;

        transaction.Postings.Single(p => p.AccountId == _food.Id).AmountMinor.Should().Be(2000);
        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(-2000);
        transaction.Payee.Should().Be("Trattoria");
    }

    [Fact]
    public void Income_debits_the_account_and_credits_the_income_category()
    {
        var transaction = LedgerTemplates.Income(
            Guid.CreateVersion7(Now), Today, "September salary", "Employer",
            _bank, _salary, Eur(300_000), Now).Value;

        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(300_000);
        transaction.Postings.Single(p => p.AccountId == _salary.Id).AmountMinor.Should().Be(-300_000);
    }

    [Fact]
    public void A_transfer_moves_money_between_two_asset_accounts_and_touches_no_category()
    {
        var transaction = LedgerTemplates.Transfer(
            Guid.CreateVersion7(Now), Today, "To savings", _bank, _savings, Eur(50_000), Now).Value;

        transaction.Postings.Single(p => p.AccountId == _savings.Id).AmountMinor.Should().Be(50_000);
        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(-50_000);
        transaction.Postings.Should().HaveCount(2);
    }

    [Fact]
    public void An_opening_balance_debits_the_account_and_credits_equity()
    {
        var transaction = LedgerTemplates.OpeningBalance(
            Guid.CreateVersion7(Now), Today, _bank, _opening, Eur(100_000), Now).Value;

        transaction.Postings.Single(p => p.AccountId == _bank.Id).AmountMinor.Should().Be(100_000);
        transaction.Postings.Single(p => p.AccountId == _opening.Id).AmountMinor.Should().Be(-100_000);
        transaction.Description.Should().Be("Opening balance");
    }

    [Fact]
    public void An_expense_against_something_that_is_not_a_category_is_rejected()
    {
        LedgerTemplates.Expense(Guid.CreateVersion7(Now), Today, "Dinner", null,
                                _bank, _savings, Eur(2000), Now)
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public void An_expense_against_an_income_category_is_rejected()
    {
        LedgerTemplates.Expense(Guid.CreateVersion7(Now), Today, "Dinner", null,
                                _bank, _salary, Eur(2000), Now)
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public void A_transfer_into_a_category_is_rejected()
    {
        LedgerTemplates.Transfer(Guid.CreateVersion7(Now), Today, "Oops", _bank, _food, Eur(100), Now)
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public void A_negative_or_zero_amount_is_rejected_by_every_template()
    {
        LedgerTemplates.Expense(Guid.CreateVersion7(Now), Today, "Dinner", null,
                                _bank, _food, Eur(0), Now)
            .Error!.Code.Should().Be("transaction.zero_amount");

        LedgerTemplates.Transfer(Guid.CreateVersion7(Now), Today, "To savings",
                                 _bank, _savings, Eur(-1), Now)
            .Error!.Code.Should().Be("transaction.zero_amount");
    }
}
```

- [x] **Step 2: Write the failing I12 property test**

```csharp
using CsCheck;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Periods;
using Money.TestSupport;

namespace Money.Domain.Tests.Ledger;

/// <summary>
/// Invariant I12: spending reports include only Kind=Expense accounts, so transfers, pocket
/// funding and investment purchases are structurally excluded.
/// </summary>
public sealed class SpendingExclusionPropertyTests
{
    private static readonly DateRange Whole2026 =
        DateRange.Create(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)).Value;

    [Fact]
    public void Total_spending_equals_the_sum_of_entries_on_expense_accounts_and_nothing_else()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var expected = ledger.Transactions
                .Where(t => !t.IsVoided && Whole2026.Contains(t.OccurredOn))
                .SelectMany(t => t.Postings)
                .Where(p => ledger.AccountsById[p.AccountId].Kind == AccountKind.Expense)
                .Sum(p => p.AmountMinor);

            var actual = BalanceCalculator
                .TotalSpending(Currency.Eur, ledger.AccountsById, ledger.Transactions, Whole2026)
                .AmountMinor;

            return actual == expected;
        }, iter: 2_000);
    }

    [Fact]
    public void Moving_money_into_savings_never_changes_reported_spending()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var before = BalanceCalculator
                .TotalSpending(Currency.Eur, ledger.AccountsById, ledger.Transactions, Whole2026);

            var extra = LedgerTemplates.Transfer(
                Guid.CreateVersion7(), new DateOnly(2026, 6, 15), "To savings",
                ledger.Bank, ledger.Savings,
                Money.Domain.Money.Money.Of(123_456, Currency.Eur),
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)).Value;

            var after = BalanceCalculator.TotalSpending(
                Currency.Eur, ledger.AccountsById,
                ledger.Transactions.Append(extra), Whole2026);

            return after == before;
        }, iter: 2_000);
    }

    [Fact]
    public void Buying_an_investment_never_changes_reported_spending()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var before = BalanceCalculator
                .TotalSpending(Currency.Eur, ledger.AccountsById, ledger.Transactions, Whole2026);

            var extra = LedgerTemplates.Transfer(
                Guid.CreateVersion7(), new DateOnly(2026, 6, 15), "Buy deposit",
                ledger.Bank, ledger.Investment,
                Money.Domain.Money.Money.Of(999_999, Currency.Eur),
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)).Value;

            var after = BalanceCalculator.TotalSpending(
                Currency.Eur, ledger.AccountsById,
                ledger.Transactions.Append(extra), Whole2026);

            return after == before;
        }, iter: 2_000);
    }

    [Fact]
    public void Receiving_salary_never_changes_reported_spending()
    {
        LedgerGen.Ledgers.Sample(ledger =>
        {
            var before = BalanceCalculator
                .TotalSpending(Currency.Eur, ledger.AccountsById, ledger.Transactions, Whole2026);

            var extra = LedgerTemplates.Income(
                Guid.CreateVersion7(), new DateOnly(2026, 6, 15), "Salary", null,
                ledger.Bank, ledger.Salary,
                Money.Domain.Money.Money.Of(300_000, Currency.Eur),
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)).Value;

            var after = BalanceCalculator.TotalSpending(
                Currency.Eur, ledger.AccountsById,
                ledger.Transactions.Append(extra), Whole2026);

            return after == before;
        }, iter: 2_000);
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Domain.Tests --filter Ledger`
Expected: FAIL — `LedgerTemplates` does not exist.

- [x] **Step 4: Implement `LedgerTemplates`**

```csharp
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
```

`OpeningBalance` allows a negative amount (an account that starts overdrawn) while the other three do not; that is why its guard is `IsZero` rather than `Sign <= 0`.

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Domain.Tests`
Expected: PASS — everything, including the four I12 properties.

- [x] **Step 6: Run the mutation gate**

Run: `dotnet stryker` from `src/Money.Domain`
Expected: score at or above 80. `Transaction.BuildPostings` and `BalanceCalculator` are the highest-value targets — every surviving mutant there is a real hole in the money logic. Fix by adding assertions, not by lowering the threshold.

- [x] **Step 7: Commit**

```bash
git add src/Money.Domain/Ledger tests/Money.Domain.Tests/Ledger
git commit -m "feat: add LedgerTemplates with I12 spending-exclusion property tests"
```

**Phase 1 acceptance:** I1, I2, I3, I4, I5, I11 and I12 all hold — I1, I3, I11 and I12 under CsCheck property tests over generated ledgers containing transfers, savings funding and investment purchases. `dotnet test` and `dotnet stryker` pass. `Money.Domain` still has zero external dependencies.

---

# Phase 2 — Persistence and the shell

**Deliverable:** EF Core, migrations, SQLite, backup, export, integrity check, Minimal API, HTMX shell, transactions and categories screens, first-run wizard.
**Acceptance:** **a usable manual expense tracker.** Round-trip: add an expense, see it in the list, see the balance change.

This is the phase that turns a proven domain into something the user opens every day. The order below is deliberate: ports and the display mapper first (so nothing downstream invents its own sign handling), then the database, then use cases, then HTTP, then HTML.

---

### Task 17: Application ports, DTOs and the one sign-convention mapper

**Files:**
- Create: `src/Money.Application/Abstractions/IUnitOfWork.cs`, `IAccountRepository.cs`, `ITransactionRepository.cs`, `ILedgerQueries.cs`, `ISettingsRepository.cs`, `IIdempotencyStore.cs`, `IBackupService.cs`, `IExportService.cs`, `IIntegrityChecker.cs`, `ICurrentUser.cs`
- Create: `src/Money.Application/Contracts/AccountContracts.cs`, `CategoryContracts.cs`, `TransactionContracts.cs`, `SettingsContracts.cs`, `AdminContracts.cs`
- Create: `src/Money.Application/Presentation/DisplayAmountMapper.cs`
- Delete: `src/Money.Application/ApplicationAssemblyMarker.cs` (replaced by real types — update `DependencyRuleTests` to point at `typeof(Money.Application.Abstractions.IUnitOfWork)`)
- Test: `tests/Money.Application.Tests/Presentation/DisplayAmountMapperTests.cs`

**Interfaces:**
- Consumes: `Account`, `Transaction`, `AccountKind`, `AccountRole`, `Currency`, `Money`.
- Produces (ports):
  - `interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct = default); }`
  - `interface IAccountRepository` — `Task<Account?> FindAsync(Guid, CancellationToken)`, `Task<Account?> FindByPathAsync(string, CancellationToken)`, `Task<Account?> FindFirstByRoleAsync(AccountRole, CancellationToken)`, `Task<IReadOnlyList<Account>> ListAsync(AccountKind?, AccountRole?, bool includeArchived, CancellationToken)`, `Task<IReadOnlyList<Account>> ListAllAsync(CancellationToken)`, `Task<IReadOnlyList<Account>> ChildrenOfAsync(Guid? parentId, CancellationToken)`, `Task<IReadOnlyList<Account>> DescendantsOfAsync(string pathPrefix, CancellationToken)`, `void Add(Account)`
  - `interface ITransactionRepository` — `Task<Transaction?> FindAsync(Guid, CancellationToken)`, `Task<IReadOnlyList<Transaction>> ListAllAsync(CancellationToken)`, `void Add(Transaction)`
  - `interface ILedgerQueries` — `Task<long> BalanceOfAsync(Guid accountId, DateOnly? asOfInclusive, CancellationToken)`, `Task<long> SubtreeBalanceAsync(string path, DateOnly? fromInclusive, DateOnly? toExclusive, CancellationToken)`, `Task<TransactionPage> ListAsync(TransactionQuery, CancellationToken)`, `Task<IReadOnlyList<AccountBalanceRow>> AllBalancesAsync(CancellationToken)`
  - `interface ISettingsRepository` — `Task<AppSettings?> GetAsync(CancellationToken)`, `Task SaveAsync(AppSettings, CancellationToken)`
  - `interface IIdempotencyStore` — `Task<string?> TryGetResponseAsync(string key, string endpoint, CancellationToken)`, `Task RecordAsync(string key, string endpoint, string responseJson, DateTimeOffset createdAtUtc, CancellationToken)`
  - `interface IBackupService` — `Task<string> CreateBackupAsync(CancellationToken)`, `Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken)`
  - `interface IExportService` — `Task<string> ExportJsonAsync(CancellationToken)`, `Task<string> ExportCsvAsync(CancellationToken)`
  - `interface IIntegrityChecker` — `Task<IntegrityReport> CheckAsync(CancellationToken)`
  - `interface ICurrentUser { string DisplayName { get; } }`
- Produces (the mapper): `static class DisplayAmountMapper` with `static bool IsNegatedForDisplay(AccountKind)`, `static decimal ToDisplay(long amountMinor, AccountKind, Currency)`, `static long ToStored(decimal displayed, AccountKind, Currency)`.

**This mapper is the only place in the solution where the sign convention leaks.** Every DTO amount goes through it. If you find a `* -1` anywhere else, that is a bug.

- [x] **Step 1: Write the failing mapper tests**

```csharp
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Money;

namespace Money.Application.Tests.Presentation;

public sealed class DisplayAmountMapperTests
{
    [Theory]
    [InlineData(AccountKind.Asset, false)]
    [InlineData(AccountKind.Expense, false)]
    [InlineData(AccountKind.Income, true)]
    [InlineData(AccountKind.Liability, true)]
    [InlineData(AccountKind.Equity, true)]
    public void Only_income_liability_and_equity_are_negated_for_display(AccountKind kind, bool negated)
    {
        DisplayAmountMapper.IsNegatedForDisplay(kind).Should().Be(negated);
    }

    [Fact]
    public void A_salary_stored_as_a_credit_is_displayed_as_a_positive_number()
    {
        // Internally: Income:Salary -300000. The user must see 3000.00.
        DisplayAmountMapper.ToDisplay(-300_000, AccountKind.Income, Currency.Eur).Should().Be(3000.00m);
    }

    [Fact]
    public void An_expense_stored_as_a_debit_is_displayed_as_a_positive_number()
    {
        DisplayAmountMapper.ToDisplay(2000, AccountKind.Expense, Currency.Eur).Should().Be(20.00m);
    }

    [Fact]
    public void A_bank_balance_keeps_its_natural_sign()
    {
        DisplayAmountMapper.ToDisplay(100_000, AccountKind.Asset, Currency.Eur).Should().Be(1000.00m);
        DisplayAmountMapper.ToDisplay(-500, AccountKind.Asset, Currency.Eur).Should().Be(-5.00m);
    }

    [Theory]
    [InlineData(AccountKind.Asset)]
    [InlineData(AccountKind.Expense)]
    [InlineData(AccountKind.Income)]
    [InlineData(AccountKind.Liability)]
    [InlineData(AccountKind.Equity)]
    public void Converting_to_display_and_back_returns_the_stored_amount(AccountKind kind)
    {
        foreach (var stored in new long[] { -300_000, -1, 0, 1, 2345, 999_999 })
        {
            var displayed = DisplayAmountMapper.ToDisplay(stored, kind, Currency.Eur);
            DisplayAmountMapper.ToStored(displayed, kind, Currency.Eur).Should().Be(stored);
        }
    }

    [Fact]
    public void A_zero_decimal_currency_is_not_scaled()
    {
        var jpy = Currency.FromCode("JPY").Value;

        DisplayAmountMapper.ToDisplay(2345, AccountKind.Expense, jpy).Should().Be(2345m);
        DisplayAmountMapper.ToStored(2345m, AccountKind.Expense, jpy).Should().Be(2345);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter DisplayAmountMapperTests`
Expected: FAIL — `DisplayAmountMapper` does not exist.

- [x] **Step 3: Implement the mapper**

```csharp
using Money.Domain.Accounts;
using Money.Domain.Money;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Presentation;

/// <summary>
/// The single place the ledger's sign convention is translated for humans.
///
/// Internally: positive = debit. Asset and Expense grow positive; Income, Liability and Equity
/// grow negative. A user asked "how much did I earn?" expects a positive number, so those three
/// kinds are negated here - and nowhere else. If you find another sign flip in the codebase,
/// it is a bug.
/// </summary>
public static class DisplayAmountMapper
{
    public static bool IsNegatedForDisplay(AccountKind kind) =>
        kind is AccountKind.Income or AccountKind.Liability or AccountKind.Equity;

    public static decimal ToDisplay(long amountMinor, AccountKind kind, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var oriented = IsNegatedForDisplay(kind) ? -amountMinor : amountMinor;
        return oriented / currency.MinorUnitScale;
    }

    public static long ToStored(decimal displayed, AccountKind kind, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var minor = MoneyValue.RoundToMinor(displayed * currency.MinorUnitScale);
        return IsNegatedForDisplay(kind) ? -minor : minor;
    }
}
```

- [x] **Step 4: Write the ports**

One file each under `Abstractions/`. Repositories take domain entities; nothing here mentions EF Core.

```csharp
namespace Money.Application.Abstractions;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

```csharp
using Money.Domain.Accounts;

namespace Money.Application.Abstractions;

public interface IAccountRepository
{
    Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Account?> FindByPathAsync(string path, CancellationToken cancellationToken = default);

    Task<Account?> FindFirstByRoleAsync(AccountRole role, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> ListAsync(
        AccountKind? kind, AccountRole? role, bool includeArchived,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> ChildrenOfAsync(Guid? parentId, CancellationToken cancellationToken = default);

    /// <summary>Everything strictly below <paramref name="pathPrefix"/> (which must end in '/').</summary>
    Task<IReadOnlyList<Account>> DescendantsOfAsync(string pathPrefix, CancellationToken cancellationToken = default);

    void Add(Account account);
}
```

```csharp
using Money.Domain.Ledger;

namespace Money.Application.Abstractions;

public interface ITransactionRepository
{
    Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transaction>> ListAllAsync(CancellationToken cancellationToken = default);

    void Add(Transaction transaction);
}
```

```csharp
namespace Money.Application.Abstractions;

public sealed record AccountBalanceRow(Guid AccountId, long BalanceMinor);

public sealed record TransactionQuery(
    DateOnly? From,
    DateOnly? To,
    Guid? AccountId,
    Guid? CategoryId,
    string? Text,
    bool IncludeVoided,
    string? Cursor,
    int Limit);

public sealed record TransactionRow(
    Guid Id,
    DateOnly OccurredOn,
    string Description,
    string? Payee,
    bool IsVoided,
    string CurrencyCode,
    long SignedAmountMinor,
    string? CategoryName,
    string? AccountName);

public sealed record TransactionPage(IReadOnlyList<TransactionRow> Rows, string? NextCursor);

public interface ILedgerQueries
{
    Task<long> BalanceOfAsync(
        Guid accountId, DateOnly? asOfInclusive, CancellationToken cancellationToken = default);

    /// <summary>Sums the subtree rooted at <paramref name="path"/>, inclusive of the root itself.</summary>
    Task<long> SubtreeBalanceAsync(
        string path, DateOnly? fromInclusive, DateOnly? toExclusive,
        CancellationToken cancellationToken = default);

    Task<TransactionPage> ListAsync(TransactionQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountBalanceRow>> AllBalancesAsync(CancellationToken cancellationToken = default);
}
```

```csharp
using Money.Domain.Periods;

namespace Money.Application.Abstractions;

/// <summary>The settings row, as the application layer sees it.</summary>
public sealed record AppSettings(
    string BaseCurrencyCode,
    PeriodDefinition PeriodDefinition,
    int BackupRetentionCount,
    bool FirstRunCompleted);

public interface ISettingsRepository
{
    Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
```

```csharp
namespace Money.Application.Abstractions;

public interface IIdempotencyStore
{
    Task<string?> TryGetResponseAsync(string key, string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// The timestamp is passed in rather than read here: no component below the composition root
    /// may read ambient time.
    /// </summary>
    Task RecordAsync(
        string key, string endpoint, string responseJson, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);
}
```

```csharp
namespace Money.Application.Abstractions;

public sealed record BackupInfo(string FileName, DateTimeOffset CreatedAtUtc, long SizeBytes);

public interface IBackupService
{
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);
}
```

```csharp
namespace Money.Application.Abstractions;

public interface IExportService
{
    Task<string> ExportJsonAsync(CancellationToken cancellationToken = default);

    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);
}
```

```csharp
namespace Money.Application.Abstractions;

public sealed record IntegrityFinding(string Check, string Detail);

public sealed record IntegrityReport(bool IsHealthy, IReadOnlyList<IntegrityFinding> Findings);

public interface IIntegrityChecker
{
    Task<IntegrityReport> CheckAsync(CancellationToken cancellationToken = default);
}
```

```csharp
namespace Money.Application.Abstractions;

/// <summary>
/// Present so that server mode (phase 9) is a registration change. There are deliberately no
/// OwnerId columns anywhere (spec D12).
/// </summary>
public interface ICurrentUser
{
    string DisplayName { get; }
}
```

- [x] **Step 5: Write the contracts (DTOs)**

```csharp
namespace Money.Application.Contracts;

public sealed record AccountDto(
    Guid Id, string Name, string Kind, string Role, Guid? ParentAccountId,
    string Path, string CurrencyCode, bool IsArchived,
    int SortOrder, string? ColorHex, string? Icon, string? Notes);

public sealed record AccountBalanceDto(
    Guid AccountId, string Name, decimal Balance, string CurrencyCode, DateOnly? AsOf);

public sealed record CreateAccountRequest(
    string Name, string Kind, string Role, Guid? ParentAccountId,
    string? CurrencyCode, decimal? OpeningBalance, DateOnly? OpenedOn);

public sealed record PatchAccountRequest(
    string? Name, Guid? ParentAccountId, bool? ClearParent,
    int? SortOrder, string? ColorHex, string? Icon, string? Notes);
```

```csharp
namespace Money.Application.Contracts;

public sealed record CategoryNodeDto(
    Guid Id, string Name, string Path, string Kind, bool IsArchived,
    string? ColorHex, string? Icon, IReadOnlyList<CategoryNodeDto> Children);

public sealed record CreateCategoryRequest(string Name, string Kind, Guid? ParentCategoryId);
```

```csharp
namespace Money.Application.Contracts;

public sealed record TransactionLineRequest(Guid AccountId, decimal Amount, string? Memo);

public sealed record CreateTransactionRequest(
    DateOnly OccurredOn, string Description, string? Payee,
    IReadOnlyList<TransactionLineRequest> Lines);

public sealed record TransactionLineDto(
    Guid AccountId, string AccountName, string AccountPath, string AccountKind,
    decimal Amount, string CurrencyCode, string? Memo);

public sealed record TransactionDto(
    Guid Id, DateOnly OccurredOn, string Description, string? Payee,
    string SourceKind, bool IsVoided, string? VoidReason,
    IReadOnlyList<TransactionLineDto> Lines);

public sealed record TransactionListItemDto(
    Guid Id, DateOnly OccurredOn, string Description, string? Payee,
    bool IsVoided, decimal Amount, string CurrencyCode,
    string? CategoryName, string? AccountName);

public sealed record TransactionPageDto(IReadOnlyList<TransactionListItemDto> Items, string? NextCursor);

public sealed record QuickExpenseRequest(
    decimal Amount, Guid CategoryId, Guid AccountId, DateOnly? OccurredOn,
    string? Description, string? Payee);

public sealed record TransferRequest(
    decimal Amount, Guid FromAccountId, Guid ToAccountId, DateOnly? OccurredOn, string? Description);

public sealed record VoidTransactionRequest(string Reason);
```

```csharp
namespace Money.Application.Contracts;

public sealed record SettingsDto(
    string BaseCurrencyCode, string PeriodAnchor, int PeriodAnchorDay,
    string TimeZoneId, string FirstDayOfWeek, int BackupRetentionCount, bool FirstRunCompleted);

public sealed record UpdateSettingsRequest(
    string BaseCurrencyCode, string PeriodAnchor, int PeriodAnchorDay,
    string TimeZoneId, string FirstDayOfWeek, int BackupRetentionCount);

public sealed record FirstRunRequest(
    string BaseCurrencyCode, string PeriodAnchor, int PeriodAnchorDay,
    string TimeZoneId, string FirstDayOfWeek,
    string FirstAccountName, string FirstAccountRole, decimal OpeningBalance, DateOnly OpenedOn,
    bool SeedStarterCategories);
```

```csharp
namespace Money.Application.Contracts;

public sealed record BackupResultDto(string FileName, DateTimeOffset CreatedAtUtc, long SizeBytes);

public sealed record IntegrityReportDto(bool IsHealthy, IReadOnlyList<string> Findings);
```

- [x] **Step 6: Point the architecture test at a real type and delete the marker**

In `DependencyRuleTests`, replace `typeof(Money.Application.ApplicationAssemblyMarker)` with `typeof(Money.Application.Abstractions.IUnitOfWork)`, then delete `ApplicationAssemblyMarker.cs`.

- [x] **Step 7: Run the tests**

Run: `dotnet test`
Expected: PASS — including the architecture tests against the new application types.

- [x] **Step 8: Commit**

```bash
git add src/Money.Application tests/Money.Application.Tests tests/Money.Architecture.Tests
git commit -m "feat: add application ports, contracts and the single display-amount mapper"
```

---

### Task 18: EF Core model, check constraints, indexes and the first migration

**Files:**
- Create: `src/Money.Infrastructure/Persistence/MoneyDbContext.cs`, `SettingsEntity.cs`, `IdempotencyRecord.cs`
- Create: `src/Money.Infrastructure/Persistence/Configurations/AccountConfiguration.cs`, `TransactionConfiguration.cs`, `PostingConfiguration.cs`, `SettingsConfiguration.cs`, `IdempotencyConfiguration.cs`
- Create: `src/Money.Infrastructure/Persistence/Migrations/*` (generated)
- Modify: `src/Money.Infrastructure/Money.Infrastructure.csproj` (add EF packages)
- Test: `tests/Money.Application.Tests/Persistence/SchemaConstraintTests.cs`
- Create: `tests/Money.TestSupport/SqliteFixture.cs`

**Interfaces:**
- Consumes: `Account`, `Transaction`, `Posting`.
- Produces:
  - `sealed class MoneyDbContext : DbContext` with `DbSet<Account> Accounts`, `DbSet<Transaction> Transactions`, `DbSet<Posting> Postings`, `DbSet<SettingsEntity> Settings`, `DbSet<IdempotencyRecord> IdempotencyRecords`
  - `sealed class SqliteFixture : IDisposable` in `Money.TestSupport` with `MoneyDbContext NewContext()` and `SqliteConnection Connection` — a real SQLite `:memory:` database with the connection held open and migrations applied.

Every constraint listed in spec section 8 is created here. The reason they are database constraints and not just domain checks is that the domain cannot see concurrent writers, and phase 3's recurring materialiser depends on the database refusing a duplicate.

- [x] **Step 1: Add the EF packages**

Add to `src/Money.Infrastructure/Money.Infrastructure.csproj`:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
  </ItemGroup>
```

Install the EF tooling if it is not present:

```bash
dotnet tool install --local dotnet-ef
```

- [x] **Step 2: Write the failing schema tests**

These are the tests that prove the *database*, not the domain, refuses bad data. They must run against real SQLite.

```csharp
using Microsoft.Data.Sqlite;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

public sealed class SchemaConstraintTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private int ExecuteRaw(string sql)
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    [Fact]
    public void The_schema_has_the_expected_tables()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        using var reader = command.ExecuteReader();

        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));

        tables.Should().Contain(["Accounts", "Transactions", "Postings", "Settings", "IdempotencyRecords"]);
    }

    [Fact]
    public void A_posting_of_zero_is_refused_by_the_database()
    {
        SeedOneAccountAndTransaction(out var accountId, out var transactionId);

        var act = () => ExecuteRaw(
            $"INSERT INTO Postings (Id, TransactionId, AccountId, AmountMinor, CurrencyCode) " +
            $"VALUES ('{Guid.NewGuid()}', '{transactionId}', '{accountId}', 0, 'EUR')");

        act.Should().Throw<SqliteException>().WithMessage("*CHECK constraint failed*");
    }

    [Fact]
    public void An_account_with_an_illegal_kind_and_role_pair_is_refused_by_the_database()
    {
        var act = () => ExecuteRaw(
            "INSERT INTO Accounts (Id, Name, Kind, Role, Path, CurrencyCode, IsArchived, SortOrder, " +
            "CreatedAtUtc, UpdatedAtUtc) VALUES " +
            $"('{Guid.NewGuid()}', 'Bad', 'Asset', 'Category', '/asset/bad', 'EUR', 0, 0, " +
            "'2026-09-01T00:00:00+00:00', '2026-09-01T00:00:00+00:00')");

        act.Should().Throw<SqliteException>().WithMessage("*CHECK constraint failed*");
    }

    [Fact]
    public void Two_accounts_cannot_share_a_path()
    {
        ExecuteRaw(InsertAccountSql(Guid.NewGuid(), "Food", "Expense", "Category", "/expense/food"));

        var act = () => ExecuteRaw(
            InsertAccountSql(Guid.NewGuid(), "Food again", "Expense", "Category", "/expense/food"));

        act.Should().Throw<SqliteException>().WithMessage("*UNIQUE constraint failed*");
    }

    [Fact]
    public void Two_recurring_transactions_from_the_same_rule_on_the_same_day_are_refused()
    {
        // This is the idempotency guard phase 3 depends on. It must exist and work before
        // any recurring code is written.
        var ruleId = Guid.NewGuid();
        ExecuteRaw(InsertTransactionSql(Guid.NewGuid(), "2026-09-01", "Rent", "Recurring", ruleId));

        var act = () => ExecuteRaw(
            InsertTransactionSql(Guid.NewGuid(), "2026-09-01", "Rent again", "Recurring", ruleId));

        act.Should().Throw<SqliteException>().WithMessage("*UNIQUE constraint failed*");
    }

    [Fact]
    public void Two_manual_transactions_on_the_same_day_are_allowed()
    {
        // The idempotency index is partial: it must not stop the user entering two coffees.
        ExecuteRaw(InsertTransactionSql(Guid.NewGuid(), "2026-09-01", "Coffee", "Manual", null));
        var act = () => ExecuteRaw(
            InsertTransactionSql(Guid.NewGuid(), "2026-09-01", "Coffee again", "Manual", null));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_posting_pointing_at_no_transaction_is_refused()
    {
        var act = () => ExecuteRaw(
            "INSERT INTO Postings (Id, TransactionId, AccountId, AmountMinor, CurrencyCode) VALUES " +
            $"('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', 100, 'EUR')");

        act.Should().Throw<SqliteException>().WithMessage("*FOREIGN KEY constraint failed*");
    }

    [Fact]
    public void No_column_in_the_schema_uses_the_real_type()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText =
            "SELECT m.name, p.name, p.type FROM sqlite_master m " +
            "JOIN pragma_table_info(m.name) p WHERE m.type = 'table'";
        using var reader = command.ExecuteReader();

        var offenders = new List<string>();
        while (reader.Read())
        {
            if (reader.GetString(2).Contains("REAL", StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        offenders.Should().BeEmpty("spec D9: money and rates never touch REAL");
    }

    private void SeedOneAccountAndTransaction(out Guid accountId, out Guid transactionId)
    {
        accountId = Guid.NewGuid();
        transactionId = Guid.NewGuid();
        ExecuteRaw(InsertAccountSql(accountId, "Food", "Expense", "Category", "/expense/food"));
        ExecuteRaw(InsertTransactionSql(transactionId, "2026-09-01", "Dinner", "Manual", null));
    }

    private static string InsertAccountSql(Guid id, string name, string kind, string role, string path) =>
        "INSERT INTO Accounts (Id, Name, Kind, Role, Path, CurrencyCode, IsArchived, SortOrder, " +
        $"CreatedAtUtc, UpdatedAtUtc) VALUES ('{id}', '{name}', '{kind}', '{role}', '{path}', 'EUR', 0, 0, " +
        "'2026-09-01T00:00:00+00:00', '2026-09-01T00:00:00+00:00')";

    private static string InsertTransactionSql(
        Guid id, string occurredOn, string description, string sourceKind, Guid? sourceId) =>
        "INSERT INTO Transactions (Id, OccurredOn, BookedAtUtc, Description, SourceKind, SourceId, " +
        "IsVoided, CreatedAtUtc, UpdatedAtUtc) VALUES " +
        $"('{id}', '{occurredOn}', '2026-09-01T00:00:00+00:00', '{description}', '{sourceKind}', " +
        $"{(sourceId is null ? "NULL" : $"'{sourceId}'")}, 0, " +
        "'2026-09-01T00:00:00+00:00', '2026-09-01T00:00:00+00:00')";
}
```

- [x] **Step 3: Write the SQLite fixture in `Money.TestSupport`**

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.Infrastructure.Persistence;

namespace Money.TestSupport;

/// <summary>
/// A real SQLite database in memory, with the connection held open for its lifetime and all
/// migrations applied. An in-memory EF *provider* is deliberately not used: only real SQLite
/// runs the check constraints and unique indexes that carry part of the safety argument.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    public SqliteFixture()
    {
        Connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        Connection.Open();

        using var context = NewContext();
        context.Database.Migrate();
    }

    public SqliteConnection Connection { get; }

    public MoneyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<MoneyDbContext>().UseSqlite(Connection).Options);

    public void Dispose() => Connection.Dispose();
}
```

`Money.TestSupport` already references `Money.Infrastructure`, so this compiles once the DbContext exists.

- [x] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter SchemaConstraintTests`
Expected: FAIL — `MoneyDbContext` does not exist.

- [x] **Step 5: Write the two infrastructure-only entities**

```csharp
namespace Money.Infrastructure.Persistence;

/// <summary>The single settings row. Id is always 1.</summary>
public sealed class SettingsEntity
{
    public int Id { get; set; } = 1;
    public string BaseCurrencyCode { get; set; } = "EUR";
    public string PeriodAnchor { get; set; } = "CalendarMonth";
    public int PeriodAnchorDay { get; set; } = 1;
    public string TimeZoneId { get; set; } = "Europe/Budapest";
    public string FirstDayOfWeek { get; set; } = nameof(DayOfWeek.Monday);
    public int BackupRetentionCount { get; set; } = 10;
    public bool FirstRunCompleted { get; set; }

    // Reserved for phase 8's window-state persistence; unused until then.
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
```

```csharp
namespace Money.Infrastructure.Persistence;

/// <summary>
/// Replays the response of a POST that carried an Idempotency-Key. One table and one filter is
/// the whole cost of surviving a phone retrying on a flaky connection (spec section 9).
/// </summary>
public sealed class IdempotencyRecord
{
    public string Key { get; set; } = null!;
    public string Endpoint { get; set; } = null!;
    public string ResponseJson { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
```

- [x] **Step 6: Write `MoneyDbContext` and the configurations**

```csharp
using Microsoft.EntityFrameworkCore;
using Money.Domain.Accounts;
using Money.Domain.Ledger;

namespace Money.Infrastructure.Persistence;

public sealed class MoneyDbContext(DbContextOptions<MoneyDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Posting> Postings => Set<Posting>();
    public DbSet<SettingsEntity> Settings => Set<SettingsEntity>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MoneyDbContext).Assembly);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Money.Domain.Accounts;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", table =>
        {
            // Spec 5.2, enforced in the domain AND here.
            table.HasCheckConstraint("CK_Accounts_KindRole",
                "(Role = 'Category' AND Kind IN ('Income','Expense')) OR " +
                "(Role IN ('Bank','Cash','SavingsPocket','Investment') AND Kind = 'Asset') OR " +
                "(Role IN ('OpeningBalance','Adjustment') AND Kind = 'Equity')");

            table.HasCheckConstraint("CK_Accounts_PathShape", "Path LIKE '/%'");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(Account.MaxNameLength).IsRequired();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Path).HasMaxLength(1024).IsRequired();
        builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(a => a.ColorHex).HasMaxLength(7);
        builder.Property(a => a.Icon).HasMaxLength(64);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        builder.HasOne<Account>()
               .WithMany()
               .HasForeignKey(a => a.ParentAccountId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.Path).IsUnique();
        builder.HasIndex(a => new { a.ParentAccountId, a.Name }).IsUnique();
        builder.HasIndex(a => new { a.Kind, a.Role });
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Money.Domain.Ledger;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Description).HasMaxLength(500).IsRequired();
        builder.Property(t => t.Payee).HasMaxLength(200);
        builder.Property(t => t.ExternalRef).HasMaxLength(200);
        builder.Property(t => t.VoidReason).HasMaxLength(500);
        builder.Property(t => t.SourceKind).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasMany(t => t.Postings)
               .WithOne()
               .HasForeignKey(p => p.TransactionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Postings)
               .HasField("_postings")
               .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => t.OccurredOn);
        builder.HasIndex(t => new { t.SourceKind, t.SourceId, t.OccurredOn });

        // THE idempotency guard (spec section 8). Partial, so ordinary manual entries are
        // unaffected: two coffees on the same day must still be possible.
        builder.HasIndex(t => new { t.SourceKind, t.SourceId, t.OccurredOn })
               .IsUnique()
               .HasDatabaseName("UX_Transactions_Source_Idempotency")
               .HasFilter("SourceKind IN ('Recurring','Accrual') AND SourceId IS NOT NULL");
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Money.Domain.Ledger;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class PostingConfiguration : IEntityTypeConfiguration<Posting>
{
    public void Configure(EntityTypeBuilder<Posting> builder)
    {
        builder.ToTable("Postings", table =>
            table.HasCheckConstraint("CK_Postings_NonZero", "AmountMinor <> 0"));

        builder.HasKey(p => p.Id);

        builder.Property(p => p.AmountMinor).HasColumnType("INTEGER").IsRequired();
        builder.Property(p => p.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(p => p.Memo).HasMaxLength(500);

        builder.HasIndex(p => new { p.AccountId, p.TransactionId });

        // Covering index for balance queries: read AmountMinor straight out of the index.
        builder.HasIndex(p => p.AccountId)
               .IncludeProperties(p => new { p.AmountMinor, p.CurrencyCode, p.TransactionId })
               .HasDatabaseName("IX_Postings_Balance");
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class SettingsConfiguration : IEntityTypeConfiguration<SettingsEntity>
{
    public void Configure(EntityTypeBuilder<SettingsEntity> builder)
    {
        builder.ToTable("Settings", table =>
            table.HasCheckConstraint("CK_Settings_SingleRow", "Id = 1"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.BaseCurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(s => s.PeriodAnchor).HasMaxLength(20).IsRequired();
        builder.Property(s => s.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.FirstDayOfWeek).HasMaxLength(10).IsRequired();
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Money.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(r => new { r.Key, r.Endpoint });

        builder.Property(r => r.Key).HasMaxLength(200);
        builder.Property(r => r.Endpoint).HasMaxLength(200);
        builder.Property(r => r.ResponseJson).IsRequired();
    }
}
```

- [x] **Step 7: Add the design-time factory and generate the migration**

EF's tooling needs to build a context outside the API host. Add `src/Money.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Money.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef`. Never resolved at runtime.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MoneyDbContext>
{
    public MoneyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MoneyDbContext>()
            .UseSqlite("DataSource=design-time.db")
            .Options);
}
```

Generate:

```bash
dotnet ef migrations add InitialSchema --project src/Money.Infrastructure --output-dir Persistence/Migrations
```

Open the generated migration and **read it**. Confirm: no `REAL` columns; `AmountMinor` is `INTEGER`; the three check constraints appear; `UX_Transactions_Source_Idempotency` has its `WHERE` filter. If `IncludeProperties` produced nothing (SQLite has no INCLUDE), replace `IX_Postings_Balance` with a plain composite index on `(AccountId, TransactionId, AmountMinor)` — SQLite covers a query from any index whose columns suffice.

- [x] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Application.Tests --filter SchemaConstraintTests`
Expected: PASS, 8 tests.

If `A_posting_pointing_at_no_transaction_is_refused` fails, foreign keys are off — check that the fixture's connection string carries `Foreign Keys=True`.

- [x] **Step 9: Commit**

```bash
git add src/Money.Infrastructure tests/Money.TestSupport tests/Money.Application.Tests
git commit -m "feat: add EF Core model, database check constraints, idempotency index and initial migration"
```

---

### Task 19: Data directory, pragmas, pre-migration backup and the migration test

**Files:**
- Create: `src/Money.Infrastructure/Persistence/DataDirectory.cs`, `SqlitePragmaInterceptor.cs`, `DatabaseInitializer.cs`
- Test: `tests/Money.Application.Tests/Persistence/DataDirectoryTests.cs`, `DatabaseInitializerTests.cs`

**Interfaces:**
- Produces:
  - `static class DataDirectory` — `const string EnvironmentVariable = "MONEYAPP_DATA_DIR"`, `static string Resolve(string? overrideDirectory, string appDataRoot)`, `static string DatabasePathIn(string dataDirectory)`, `static string BackupDirectoryIn(string dataDirectory)`, `static string ConnectionStringFor(string databasePath)`
  - `sealed class SqlitePragmaInterceptor : DbConnectionInterceptor`
  - `sealed class DatabaseInitializer(MoneyDbContext, IBackupService, ILogger<DatabaseInitializer>)` — `Task InitialiseAsync(CancellationToken)`

Spec D10 and section 14: the database must not live beside the executable, because the project folder sits on the Desktop and is frequently OneDrive-synced; a synced SQLite file in WAL mode can be corrupted by the sync client.

- [x] **Step 1: Write the failing tests**

```csharp
using Money.Infrastructure.Persistence;

namespace Money.Application.Tests.Persistence;

public sealed class DataDirectoryTests
{
    [Fact]
    public void Without_an_override_the_data_directory_is_under_the_roaming_app_data_root()
    {
        DataDirectory.Resolve(overrideDirectory: null, appDataRoot: @"C:\Users\jt\AppData\Roaming")
            .Should().Be(Path.Combine(@"C:\Users\jt\AppData\Roaming", "MoneyApp"));
    }

    [Fact]
    public void An_override_wins()
    {
        DataDirectory.Resolve(@"D:\money-data", @"C:\Users\jt\AppData\Roaming")
            .Should().Be(@"D:\money-data");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_override_is_ignored(string overrideDirectory)
    {
        DataDirectory.Resolve(overrideDirectory, @"C:\Users\jt\AppData\Roaming")
            .Should().EndWith("MoneyApp");
    }

    [Fact]
    public void The_database_and_backups_live_inside_the_data_directory()
    {
        DataDirectory.DatabasePathIn(@"D:\money-data")
            .Should().Be(Path.Combine(@"D:\money-data", "money.db"));
        DataDirectory.BackupDirectoryIn(@"D:\money-data")
            .Should().Be(Path.Combine(@"D:\money-data", "backups"));
    }

    [Fact]
    public void The_connection_string_enables_foreign_keys_and_names_the_file()
    {
        var connectionString = DataDirectory.ConnectionStringFor(@"D:\money-data\money.db");

        connectionString.Should().Contain(@"D:\money-data\money.db");
        connectionString.Should().Contain("Foreign Keys=True");
    }
}
```

```csharp
using Microsoft.Data.Sqlite;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Foreign_keys_are_on_for_every_connection()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";

        Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(1);
    }

    [Fact]
    public void Applying_the_migrations_twice_is_a_no_op()
    {
        using var context = _fixture.NewContext();

        var act = () => context.Database.Migrate();

        act.Should().NotThrow();
        context.Database.GetPendingMigrations().Should().BeEmpty();
    }

    [Fact]
    public void A_freshly_migrated_database_passes_the_integrity_check()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM (SELECT TransactionId FROM Postings " +
            "GROUP BY TransactionId HAVING SUM(AmountMinor) <> 0)";

        Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(0);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter "DataDirectoryTests|DatabaseInitializerTests"`
Expected: FAIL — `DataDirectory` does not exist.

- [x] **Step 3: Implement `DataDirectory`**

```csharp
using Microsoft.Data.Sqlite;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// Where the ledger lives. Deliberately NOT beside the executable: the project folder is on the
/// Desktop, which is commonly OneDrive-synced, and a synced SQLite file in WAL mode can be
/// corrupted by the sync client (spec D10 and section 14).
/// </summary>
public static class DataDirectory
{
    public const string EnvironmentVariable = "MONEYAPP_DATA_DIR";

    public const string DatabaseFileName = "money.db";

    public const string BackupFolderName = "backups";

    public static string Resolve(string? overrideDirectory, string appDataRoot) =>
        string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(appDataRoot, "MoneyApp")
            : overrideDirectory.Trim();

    public static string DatabasePathIn(string dataDirectory) =>
        Path.Combine(dataDirectory, DatabaseFileName);

    public static string BackupDirectoryIn(string dataDirectory) =>
        Path.Combine(dataDirectory, BackupFolderName);

    public static string ConnectionStringFor(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default
        }.ToString();
}
```

- [x] **Step 4: Implement the pragma interceptor**

`journal_mode` is persistent once set, but `busy_timeout` is per-connection, so it must be applied on every open.

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Money.Infrastructure.Persistence;

/// <summary>Applies the per-connection pragmas from spec section 8 every time a connection opens.</summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(
        DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string PragmaSql =
        "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }
}
```

An in-memory database ignores `journal_mode=WAL` (it stays `memory`), which is fine — the tests assert on `foreign_keys`, which does apply.

- [x] **Step 5: Implement `DatabaseInitializer`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// Startup sequence: back up first, then migrate. Spec section 8 - a migration that goes wrong
/// must never be the only copy of the ledger.
/// </summary>
public sealed class DatabaseInitializer(
    MoneyDbContext context,
    IBackupService backupService,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

        if (pending.Length > 0 && await context.Database.CanConnectAsync(cancellationToken))
        {
            var alreadyApplied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
            if (alreadyApplied.Any())
            {
                var path = await backupService.CreateBackupAsync(cancellationToken);
                logger.LogInformation("Pre-migration backup written to {BackupPath}", path);
            }
        }

        await context.Database.MigrateAsync(cancellationToken);

        if (pending.Length > 0)
        {
            logger.LogInformation("Applied {Count} migration(s): {Migrations}",
                                  pending.Length, string.Join(", ", pending));
        }
    }
}
```

The `alreadyApplied.Any()` guard means a first run on an empty machine does not try to back up a database that does not exist yet.

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Application.Tests --filter "DataDirectoryTests|DatabaseInitializerTests"`
Expected: PASS.

The `DataDirectoryTests` use Windows-shaped literal paths. On the Linux CI runner `Path.Combine` still behaves consistently for these assertions because the expected values are also built with `Path.Combine` — except the two `[InlineData]`-free cases that hard-code separators. If CI reports a failure there, change those assertions to build the expected value with `Path.Combine` too rather than weakening the test.

- [x] **Step 7: Commit**

```bash
git add src/Money.Infrastructure tests/Money.Application.Tests
git commit -m "feat: resolve the data directory outside the project folder and apply SQLite pragmas"
```

---

### Task 20: Repositories, ledger queries and the SQL-versus-domain agreement test

**Files:**
- Create: `src/Money.Infrastructure/Persistence/Repositories/AccountRepository.cs`, `TransactionRepository.cs`, `SettingsRepository.cs`, `IdempotencyStore.cs`, `EfUnitOfWork.cs`
- Create: `src/Money.Infrastructure/Persistence/LedgerQueries.cs`, `IntegrityChecker.cs`
- Create: `tests/Money.TestSupport/LedgerSeeder.cs`
- Test: `tests/Money.Application.Tests/Persistence/LedgerQueriesTests.cs`, `IntegrityCheckerTests.cs`

**Interfaces:**
- Consumes: the ports from Task 17, `MoneyDbContext`, `BalanceCalculator`.
- Produces: EF implementations of every port, plus `sealed class LedgerSeeder` in `Money.TestSupport` with `static Task<LedgerGen.GeneratedLedger> SeedAsync(MoneyDbContext, LedgerGen.GeneratedLedger)`.

The headline test of this task: **`LedgerQueries` and `BalanceCalculator` must agree on generated ledgers.** The domain calculator is the specification; the SQL is an optimisation. If they disagree, the SQL is wrong.

- [ ] **Step 1: Write the failing agreement tests**

```csharp
using CsCheck;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Periods;
using Money.Infrastructure.Persistence;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

public sealed class LedgerQueriesTests
{
    private static async Task<(MoneyDbContext Context, LedgerQueries Queries, LedgerGen.GeneratedLedger Ledger)>
        SeedAsync(SqliteFixture fixture, LedgerGen.GeneratedLedger ledger)
    {
        var context = fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, ledger);
        return (context, new LedgerQueries(context), ledger);
    }

    [Fact]
    public async Task An_account_balance_from_sql_matches_the_domain_calculator()
    {
        foreach (var ledger in LedgerGen.Ledgers.Take(25))
        {
            using var fixture = new SqliteFixture();
            var (context, queries, seeded) = await SeedAsync(fixture, ledger);

            using (context)
            {
                foreach (var account in seeded.Accounts)
                {
                    var fromSql = await queries.BalanceOfAsync(account.Id, null);
                    var fromDomain = BalanceCalculator
                        .BalanceOf(account.Id, Currency.Eur, seeded.Transactions).AmountMinor;

                    fromSql.Should().Be(fromDomain, "account {0}", account.Path);
                }
            }
        }
    }

    [Fact]
    public async Task A_balance_as_of_a_date_from_sql_matches_the_domain_calculator()
    {
        var asOf = new DateOnly(2026, 6, 30);

        foreach (var ledger in LedgerGen.Ledgers.Take(25))
        {
            using var fixture = new SqliteFixture();
            var (context, queries, seeded) = await SeedAsync(fixture, ledger);

            using (context)
            {
                var fromSql = await queries.BalanceOfAsync(seeded.Bank.Id, asOf);
                var fromDomain = BalanceCalculator
                    .BalanceOf(seeded.Bank.Id, Currency.Eur, seeded.Transactions, asOf).AmountMinor;

                fromSql.Should().Be(fromDomain);
            }
        }
    }

    [Fact]
    public async Task A_subtree_balance_from_sql_matches_the_domain_calculator()
    {
        foreach (var ledger in LedgerGen.Ledgers.Take(25))
        {
            using var fixture = new SqliteFixture();
            var (context, queries, seeded) = await SeedAsync(fixture, ledger);

            using (context)
            {
                var fromSql = await queries.SubtreeBalanceAsync(seeded.Groceries.Path, null, null);
                var fromDomain = BalanceCalculator
                    .SubtreeBalance(seeded.Groceries, Currency.Eur, seeded.AccountsById, seeded.Transactions)
                    .AmountMinor;

                fromSql.Should().Be(fromDomain);
            }
        }
    }

    [Fact]
    public async Task A_windowed_subtree_balance_from_sql_matches_the_domain_calculator()
    {
        var window = DateRange.Create(new DateOnly(2026, 3, 1), new DateOnly(2026, 6, 1)).Value;

        foreach (var ledger in LedgerGen.Ledgers.Take(25))
        {
            using var fixture = new SqliteFixture();
            var (context, queries, seeded) = await SeedAsync(fixture, ledger);

            using (context)
            {
                var fromSql = await queries.SubtreeBalanceAsync(
                    seeded.Groceries.Path, window.Start, window.EndExclusive);
                var fromDomain = BalanceCalculator
                    .SubtreeBalance(seeded.Groceries, Currency.Eur, seeded.AccountsById,
                                    seeded.Transactions, window).AmountMinor;

                fromSql.Should().Be(fromDomain);
            }
        }
    }

    [Fact]
    public async Task A_subtree_balance_does_not_pick_up_a_sibling_with_a_similar_name()
    {
        // "/expense/groceries" must not match "/expense/groceries-online".
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        var queries = new LedgerQueries(context);
        var groceriesOnly = await queries.SubtreeBalanceAsync(ledger.Groceries.Path, null, null);
        var expected = BalanceCalculator
            .SubtreeBalance(ledger.Groceries, Currency.Eur, ledger.AccountsById, ledger.Transactions)
            .AmountMinor;

        groceriesOnly.Should().Be(expected);
    }

    [Fact]
    public async Task The_transaction_list_pages_forward_without_repeating_or_skipping()
    {
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        var queries = new LedgerQueries(context);
        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var page = await queries.ListAsync(new TransactionQuery(
                null, null, null, null, null, IncludeVoided: true, cursor, Limit: 7));

            seen.AddRange(page.Rows.Select(r => r.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Should().OnlyHaveUniqueItems();
        seen.Should().HaveCount(ledger.Transactions.Count);
    }

    [Fact]
    public async Task The_transaction_list_returns_newest_first()
    {
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var page = await new LedgerQueries(context).ListAsync(new TransactionQuery(
            null, null, null, null, null, IncludeVoided: true, null, Limit: 50));

        page.Rows.Select(r => r.OccurredOn).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task The_transaction_list_can_hide_voided_rows_and_filter_by_text_and_date()
    {
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);
        var queries = new LedgerQueries(context);

        var live = await queries.ListAsync(new TransactionQuery(
            null, null, null, null, null, IncludeVoided: false, null, 200));
        live.Rows.Should().OnlyContain(r => !r.IsVoided);

        var opening = await queries.ListAsync(new TransactionQuery(
            null, null, null, null, "Opening", IncludeVoided: true, null, 200));
        opening.Rows.Should().OnlyContain(r => r.Description.Contains("Opening"));

        var firstQuarter = await queries.ListAsync(new TransactionQuery(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null, null, null, true, null, 200));
        firstQuarter.Rows.Should().OnlyContain(r =>
            r.OccurredOn >= new DateOnly(2026, 1, 1) && r.OccurredOn <= new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task The_transaction_list_can_filter_to_one_account()
    {
        using var fixture = new SqliteFixture();
        using var context = fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        var page = await new LedgerQueries(context).ListAsync(new TransactionQuery(
            null, null, ledger.Savings.Id, null, null, IncludeVoided: true, null, 200));

        var expected = ledger.Transactions
            .Count(t => t.Postings.Any(p => p.AccountId == ledger.Savings.Id));

        page.Rows.Should().HaveCount(expected);
    }
}
```

```csharp
using Money.Infrastructure.Persistence;
using Money.TestSupport;

namespace Money.Application.Tests.Persistence;

public sealed class IntegrityCheckerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_healthy_ledger_reports_no_findings()
    {
        using var context = _fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var report = await new IntegrityChecker(context).CheckAsync();

        report.IsHealthy.Should().BeTrue();
        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unbalanced_transaction_smuggled_in_through_raw_sql_is_detected()
    {
        using var context = _fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        // The domain cannot produce this; only a corrupted file or a bad migration could.
        var victim = ledger.Transactions[0].Id;
        using (var command = _fixture.Connection.CreateCommand())
        {
            command.CommandText =
                $"UPDATE Postings SET AmountMinor = AmountMinor + 1 WHERE TransactionId = '{victim}' " +
                "AND rowid = (SELECT MIN(rowid) FROM Postings WHERE TransactionId = " +
                $"'{victim}')";
            command.ExecuteNonQuery();
        }

        var report = await new IntegrityChecker(context).CheckAsync();

        report.IsHealthy.Should().BeFalse();
        report.Findings.Should().ContainSingle(f => f.Check == "zero-sum");
        report.Findings[0].Detail.Should().Contain(victim.ToString());
    }

    [Fact]
    public async Task A_transaction_with_a_single_entry_is_detected()
    {
        using var context = _fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        var victim = ledger.Transactions[0].Id;
        using (var command = _fixture.Connection.CreateCommand())
        {
            command.CommandText =
                $"DELETE FROM Postings WHERE TransactionId = '{victim}' " +
                $"AND rowid > (SELECT MIN(rowid) FROM Postings WHERE TransactionId = '{victim}')";
            command.ExecuteNonQuery();
        }

        var report = await new IntegrityChecker(context).CheckAsync();

        report.IsHealthy.Should().BeFalse();
        report.Findings.Should().Contain(f => f.Check == "entry-count");
    }
}
```

- [ ] **Step 2: Write `LedgerSeeder` in `Money.TestSupport`**

```csharp
using Microsoft.EntityFrameworkCore;
using Money.Infrastructure.Persistence;

namespace Money.TestSupport;

public static class LedgerSeeder
{
    public static async Task<LedgerGen.GeneratedLedger> SeedAsync(
        MoneyDbContext context, LedgerGen.GeneratedLedger ledger,
        CancellationToken cancellationToken = default)
    {
        context.Accounts.AddRange(ledger.Accounts);
        context.Transactions.AddRange(ledger.Transactions);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return ledger;
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter "LedgerQueriesTests|IntegrityCheckerTests"`
Expected: FAIL — `LedgerQueries` does not exist.

- [ ] **Step 4: Implement the repositories**

```csharp
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Accounts;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository(MoneyDbContext context) : IAccountRepository
{
    public Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Account?> FindByPathAsync(string path, CancellationToken cancellationToken = default) =>
        context.Accounts.FirstOrDefaultAsync(a => a.Path == path, cancellationToken);

    public Task<Account?> FindFirstByRoleAsync(
        AccountRole role, CancellationToken cancellationToken = default) =>
        context.Accounts.Where(a => a.Role == role && !a.IsArchived)
                        .OrderBy(a => a.Path)
                        .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> ListAsync(
        AccountKind? kind, AccountRole? role, bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var query = context.Accounts.AsQueryable();

        if (kind is { } k) query = query.Where(a => a.Kind == k);
        if (role is { } r) query = query.Where(a => a.Role == r);
        if (!includeArchived) query = query.Where(a => !a.IsArchived);

        return await query.OrderBy(a => a.Path).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await context.Accounts.OrderBy(a => a.Path).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> ChildrenOfAsync(
        Guid? parentId, CancellationToken cancellationToken = default) =>
        await context.Accounts.Where(a => a.ParentAccountId == parentId)
                              .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
                              .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> DescendantsOfAsync(
        string pathPrefix, CancellationToken cancellationToken = default) =>
        await context.Accounts.Where(a => a.Path.StartsWith(pathPrefix))
                              .OrderBy(a => a.Path)
                              .ToListAsync(cancellationToken);

    public void Add(Account account) => context.Accounts.Add(account);
}
```

`StartsWith` translates to `LIKE @prefix || '%'` on SQLite, which is exactly the subtree query the spec describes.

```csharp
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Ledger;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class TransactionRepository(MoneyDbContext context) : ITransactionRepository
{
    public Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Transactions.Include(t => t.Postings)
                            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        await context.Transactions.Include(t => t.Postings)
                                  .OrderBy(t => t.OccurredOn).ThenBy(t => t.Id)
                                  .ToListAsync(cancellationToken);

    public void Add(Transaction transaction) => context.Transactions.Add(transaction);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Periods;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class SettingsRepository(MoneyDbContext context) : ISettingsRepository
{
    // The two persisted anchor names. Task 24's SettingsMapper exposes the same two strings to
    // the API, and SettingsUseCaseTests round-trips them; if these ever disagree, that test fails.
    internal const string CalendarMonthAnchorName = "CalendarMonth";
    internal const string DayOfMonthAnchorName = "DayOfMonth";

    public async Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.Settings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (row is null) return null;

        var anchor = row.PeriodAnchor == DayOfMonthAnchorName
            ? PeriodAnchor.DayOfMonth(row.PeriodAnchorDay).Value
            : PeriodAnchor.CalendarMonth;

        var definition = PeriodDefinition.Create(
            anchor, row.TimeZoneId, Enum.Parse<DayOfWeek>(row.FirstDayOfWeek)).Value;

        return new AppSettings(
            row.BaseCurrencyCode, definition, row.BackupRetentionCount, row.FirstRunCompleted);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var row = await context.Settings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        var isNew = row is null;
        row ??= new SettingsEntity { Id = 1 };

        row.BaseCurrencyCode = settings.BaseCurrencyCode;
        row.PeriodAnchor = settings.PeriodDefinition.Anchor is PeriodAnchor.DayOfMonthAnchor
            ? DayOfMonthAnchorName
            : CalendarMonthAnchorName;
        row.PeriodAnchorDay = settings.PeriodDefinition.Anchor.AnchorDay;
        row.TimeZoneId = settings.PeriodDefinition.TimeZoneId;
        row.FirstDayOfWeek = settings.PeriodDefinition.FirstDayOfWeek.ToString();
        row.BackupRetentionCount = settings.BackupRetentionCount;
        row.FirstRunCompleted = settings.FirstRunCompleted;

        if (isNew) context.Settings.Add(row);
    }
}
```

The persisted anchor names are `"CalendarMonth"` and `"DayOfMonth"` — the same two strings `SettingsEntity`'s default uses (Task 18), `SettingsMapper` exposes to the API (Task 24), and `SettingsUseCaseTests` round-trips. Three places, one pair of values, one test that proves it.

```csharp
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyStore(MoneyDbContext context) : IIdempotencyStore
{
    public async Task<string?> TryGetResponseAsync(
        string key, string endpoint, CancellationToken cancellationToken = default) =>
        (await context.IdempotencyRecords
                      .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint, cancellationToken))
        ?.ResponseJson;

    public Task RecordAsync(
        string key, string endpoint, string responseJson, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key,
            Endpoint = endpoint,
            ResponseJson = responseJson,
            CreatedAtUtc = createdAtUtc
        });

        return Task.CompletedTask;
    }
}
```

`CreatedAtUtc` is supplied by the caller — the `IdempotencyFilter` in Task 28, which has the injected clock. The store must not read ambient time.

```csharp
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class EfUnitOfWork(MoneyDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 5: Implement `LedgerQueries`**

```csharp
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// Read-model SQL. Every query here is checked against BalanceCalculator in the domain, which is
/// the specification; this class is only an optimisation over it.
/// </summary>
public sealed class LedgerQueries(MoneyDbContext context) : ILedgerQueries
{
    public async Task<long> BalanceOfAsync(
        Guid accountId, DateOnly? asOfInclusive, CancellationToken cancellationToken = default)
    {
        var query = context.Postings
            .Join(context.Transactions, p => p.TransactionId, t => t.Id, (p, t) => new { p, t })
            .Where(x => x.p.AccountId == accountId && !x.t.IsVoided);

        if (asOfInclusive is { } asOf) query = query.Where(x => x.t.OccurredOn <= asOf);

        return await query.SumAsync(x => (long?)x.p.AmountMinor, cancellationToken) ?? 0L;
    }

    public async Task<long> SubtreeBalanceAsync(
        string path, DateOnly? fromInclusive, DateOnly? toExclusive,
        CancellationToken cancellationToken = default)
    {
        var childPrefix = path + "/";

        var query = context.Postings
            .Join(context.Transactions, p => p.TransactionId, t => t.Id, (p, t) => new { p, t })
            .Join(context.Accounts, x => x.p.AccountId, a => a.Id, (x, a) => new { x.p, x.t, a })
            .Where(x => !x.t.IsVoided)
            .Where(x => x.a.Path == path || x.a.Path.StartsWith(childPrefix));

        if (fromInclusive is { } from) query = query.Where(x => x.t.OccurredOn >= from);
        if (toExclusive is { } to) query = query.Where(x => x.t.OccurredOn < to);

        return await query.SumAsync(x => (long?)x.p.AmountMinor, cancellationToken) ?? 0L;
    }

    public async Task<IReadOnlyList<AccountBalanceRow>> AllBalancesAsync(
        CancellationToken cancellationToken = default) =>
        await context.Postings
            .Join(context.Transactions, p => p.TransactionId, t => t.Id, (p, t) => new { p, t })
            .Where(x => !x.t.IsVoided)
            .GroupBy(x => x.p.AccountId)
            .Select(g => new AccountBalanceRow(g.Key, g.Sum(x => x.p.AmountMinor)))
            .ToListAsync(cancellationToken);

    public async Task<TransactionPage> ListAsync(
        TransactionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, 200);

        var transactions = context.Transactions.AsQueryable();

        if (!query.IncludeVoided) transactions = transactions.Where(t => !t.IsVoided);
        if (query.From is { } from) transactions = transactions.Where(t => t.OccurredOn >= from);
        if (query.To is { } to) transactions = transactions.Where(t => t.OccurredOn <= to);

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            transactions = transactions.Where(t =>
                EF.Functions.Like(t.Description, $"%{text}%")
                || (t.Payee != null && EF.Functions.Like(t.Payee, $"%{text}%")));
        }

        if (query.AccountId is { } accountId)
        {
            transactions = transactions.Where(t => t.Postings.Any(p => p.AccountId == accountId));
        }

        if (query.CategoryId is { } categoryId)
        {
            var category = await context.Accounts
                .Where(a => a.Id == categoryId)
                .Select(a => a.Path)
                .FirstOrDefaultAsync(cancellationToken);

            if (category is not null)
            {
                var prefix = category + "/";
                transactions = transactions.Where(t => t.Postings.Any(p =>
                    context.Accounts.Any(a => a.Id == p.AccountId
                                              && (a.Path == category || a.Path.StartsWith(prefix)))));
            }
        }

        if (DecodeCursor(query.Cursor) is { } cursor)
        {
            transactions = transactions.Where(t =>
                t.OccurredOn < cursor.OccurredOn
                || (t.OccurredOn == cursor.OccurredOn && t.Id.CompareTo(cursor.Id) < 0));
        }

        var page = await transactions
            .OrderByDescending(t => t.OccurredOn).ThenByDescending(t => t.Id)
            .Take(limit + 1)
            .Select(t => new
            {
                t.Id,
                t.OccurredOn,
                t.Description,
                t.Payee,
                t.IsVoided,
                Lines = t.Postings.Select(p => new
                {
                    p.AmountMinor,
                    p.CurrencyCode,
                    Kind = context.Accounts.First(a => a.Id == p.AccountId).Kind,
                    Name = context.Accounts.First(a => a.Id == p.AccountId).Name
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var rows = page.Take(limit).Select(t =>
        {
            var expenseLine = t.Lines.FirstOrDefault(l => l.Kind == Domain.Accounts.AccountKind.Expense);
            var assetLine = t.Lines.FirstOrDefault(l => l.Kind == Domain.Accounts.AccountKind.Asset);
            var headline = expenseLine ?? t.Lines.OrderByDescending(l => Math.Abs(l.AmountMinor)).First();

            return new TransactionRow(
                t.Id, t.OccurredOn, t.Description, t.Payee, t.IsVoided,
                headline.CurrencyCode, headline.AmountMinor,
                expenseLine?.Name, assetLine?.Name);
        }).ToList();

        var nextCursor = hasMore && rows.Count > 0
            ? EncodeCursor(rows[^1].OccurredOn, rows[^1].Id)
            : null;

        return new TransactionPage(rows, nextCursor);
    }

    private static string EncodeCursor(DateOnly occurredOn, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            occurredOn.ToString("O", CultureInfo.InvariantCulture) + "|" + id.ToString("D")));

    private static (DateOnly OccurredOn, Guid Id)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2) return null;

            return (DateOnly.ParseExact(parts[0], "O", CultureInfo.InvariantCulture), Guid.Parse(parts[1]));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }
}
```

Note `t.Id.CompareTo(cursor.Id) < 0` — EF Core translates `Guid` comparison on SQLite as a text comparison, which is consistent with the `ThenByDescending(t => t.Id)` ordering because both use the same representation. If the paging test shows repeats, switch the tiebreaker to `BookedAtUtc` and re-run.

- [ ] **Step 6: Implement `IntegrityChecker`**

```csharp
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence;

/// <summary>
/// The zero-sum invariant (I1) cannot be expressed as a SQLite row check, so it is verified by
/// this sweep at startup and on demand from the admin screen (spec section 8).
/// </summary>
public sealed class IntegrityChecker(MoneyDbContext context) : IIntegrityChecker
{
    public async Task<IntegrityReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<IntegrityFinding>();

        var unbalanced = await context.Postings
            .GroupBy(p => p.TransactionId)
            .Where(g => g.Sum(p => p.AmountMinor) != 0)
            .Select(g => g.Key)
            .Take(50)
            .ToListAsync(cancellationToken);

        findings.AddRange(unbalanced.Select(id =>
            new IntegrityFinding("zero-sum", $"Transaction {id} does not sum to zero.")));

        var tooFewEntries = await context.Postings
            .GroupBy(p => p.TransactionId)
            .Where(g => g.Count() < 2)
            .Select(g => g.Key)
            .Take(50)
            .ToListAsync(cancellationToken);

        findings.AddRange(tooFewEntries.Select(id =>
            new IntegrityFinding("entry-count", $"Transaction {id} has fewer than two entries.")));

        var orphanPostings = await context.Postings
            .Where(p => !context.Accounts.Any(a => a.Id == p.AccountId))
            .Select(p => p.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        findings.AddRange(orphanPostings.Select(id =>
            new IntegrityFinding("orphan-entry", $"Entry {id} references a missing account.")));

        var brokenPaths = await context.Accounts
            .Where(a => a.ParentAccountId != null
                        && !context.Accounts.Any(p => p.Id == a.ParentAccountId
                                                      && a.Path.StartsWith(p.Path + "/")))
            .Select(a => a.Path)
            .Take(50)
            .ToListAsync(cancellationToken);

        findings.AddRange(brokenPaths.Select(path =>
            new IntegrityFinding("account-path", $"Account path '{path}' does not match its parent.")));

        return new IntegrityReport(findings.Count == 0, findings);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Application.Tests --filter "LedgerQueriesTests|IntegrityCheckerTests"`
Expected: PASS.

The agreement tests are the ones that matter. If SQL and the domain disagree, **fix the SQL**; do not adjust `BalanceCalculator` to match.

- [ ] **Step 8: Commit**

```bash
git add src/Money.Infrastructure tests/Money.TestSupport tests/Money.Application.Tests
git commit -m "feat: add EF repositories, ledger read queries and the integrity checker"
```

---

### Task 21: Account and category use cases

**Files:**
- Create: `src/Money.Application/Accounts/CreateAccountHandler.cs`, `PatchAccountHandler.cs`, `ArchiveAccountHandler.cs`, `ListAccountsHandler.cs`, `GetAccountBalanceHandler.cs`
- Create: `src/Money.Application/Categories/CreateCategoryHandler.cs`, `GetCategoryTreeHandler.cs`
- Create: `src/Money.Application/Mapping/AccountMapper.cs`
- Test: `tests/Money.Application.Tests/Accounts/AccountUseCaseTests.cs`, `tests/Money.Application.Tests/Categories/CategoryUseCaseTests.cs`
- Create: `tests/Money.Application.Tests/UseCaseHarness.cs`

**Interfaces:**
- Consumes: `IAccountRepository`, `ILedgerQueries`, `IUnitOfWork`, `IClock`, `Account`, `AccountTree`, `LedgerTemplates`.
- Produces:
  - `sealed class CreateAccountHandler(IAccountRepository, ITransactionRepository, IUnitOfWork, IClock)` — `Task<Result<AccountDto>> HandleAsync(CreateAccountRequest, CancellationToken)`
  - `sealed class PatchAccountHandler(IAccountRepository, IUnitOfWork, IClock)` — `Task<Result<AccountDto>> HandleAsync(Guid id, PatchAccountRequest, CancellationToken)`
  - `sealed class ArchiveAccountHandler(IAccountRepository, IUnitOfWork, IClock)` — `Task<Result> HandleAsync(Guid id, CancellationToken)`
  - `sealed class ListAccountsHandler(IAccountRepository)` — `Task<IReadOnlyList<AccountDto>> HandleAsync(string? kind, string? role, bool includeArchived, CancellationToken)`
  - `sealed class GetAccountBalanceHandler(IAccountRepository, ILedgerQueries)` — `Task<Result<AccountBalanceDto>> HandleAsync(Guid id, DateOnly? asOf, CancellationToken)`
  - `sealed class CreateCategoryHandler(IAccountRepository, IUnitOfWork, IClock)` — `Task<Result<AccountDto>> HandleAsync(CreateCategoryRequest, CancellationToken)`
  - `sealed class GetCategoryTreeHandler(IAccountRepository)` — `Task<IReadOnlyList<CategoryNodeDto>> HandleAsync(string kind, bool includeArchived, CancellationToken)`
  - `static class AccountMapper` — `static AccountDto ToDto(Account)`, `static Result<AccountKind> ParseKind(string?)`, `static Result<AccountRole> ParseRole(string?)`
  - `sealed class UseCaseHarness : IDisposable` in the test project — wires a `SqliteFixture` to real repositories and a `FakeClock`.

- [ ] **Step 1: Write the use-case harness**

```csharp
using Money.Application.Abstractions;
using Money.Infrastructure.Persistence;
using Money.Infrastructure.Persistence.Repositories;
using Money.TestSupport;

namespace Money.Application.Tests;

/// <summary>
/// Real SQLite, real repositories, a fake clock. No mocks: the point of the application tests
/// is that constraints and SQL actually run.
/// </summary>
public sealed class UseCaseHarness : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public UseCaseHarness()
    {
        Context = _fixture.NewContext();
        Accounts = new AccountRepository(Context);
        Transactions = new TransactionRepository(Context);
        Settings = new SettingsRepository(Context);
        Queries = new LedgerQueries(Context);
        UnitOfWork = new EfUnitOfWork(Context);
    }

    public MoneyDbContext Context { get; }
    public IAccountRepository Accounts { get; }
    public ITransactionRepository Transactions { get; }
    public ISettingsRepository Settings { get; }
    public ILedgerQueries Queries { get; }
    public IUnitOfWork UnitOfWork { get; }
    public FakeClock Clock { get; } = FakeClock.At(2026, 9, 1, 9, 0);

    public void Dispose()
    {
        Context.Dispose();
        _fixture.Dispose();
    }
}
```

- [ ] **Step 2: Write the failing account use-case tests**

```csharp
using Money.Application.Accounts;
using Money.Application.Contracts;

namespace Money.Application.Tests.Accounts;

public sealed class AccountUseCaseTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private CreateAccountHandler Create => new(_harness.Accounts, _harness.Transactions,
                                               _harness.UnitOfWork, _harness.Clock);

    [Fact]
    public async Task Creating_a_bank_account_persists_it_with_a_path()
    {
        var result = await Create.HandleAsync(
            new CreateAccountRequest("Erste Current", "Asset", "Bank", null, "EUR", null, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().Be("/asset/erste-current");
        (await _harness.Accounts.FindAsync(result.Value.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Creating_a_bank_account_with_an_opening_balance_posts_the_opening_transaction()
    {
        var result = await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", 250.00m, new DateOnly(2026, 1, 1)),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(result.Value.Id, null)).Should().Be(25_000);

        // The counter-entry is an Equity/OpeningBalance account, created on demand.
        var equity = await _harness.Accounts.FindFirstByRoleAsync(Domain.Accounts.AccountRole.OpeningBalance);
        equity.Should().NotBeNull();
        (await _harness.Queries.BalanceOfAsync(equity!.Id, null)).Should().Be(-25_000);
    }

    [Fact]
    public async Task An_opening_balance_never_shows_up_as_spending()
    {
        var account = (await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", 250.00m, new DateOnly(2026, 1, 1)),
            TestContext.Current.CancellationToken)).Value;

        var expenseTotal = await _harness.Queries.SubtreeBalanceAsync("/expense", null, null);

        expenseTotal.Should().Be(0);
        account.Should().NotBeNull();
    }

    [Fact]
    public async Task An_illegal_kind_and_role_pair_is_rejected_before_it_reaches_the_database()
    {
        var result = await Create.HandleAsync(
            new CreateAccountRequest("Nonsense", "Asset", "Category", null, "EUR", null, null),
            TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task An_unparsable_kind_is_rejected_with_a_useful_code()
    {
        var result = await Create.HandleAsync(
            new CreateAccountRequest("Nonsense", "Bananas", "Bank", null, "EUR", null, null),
            TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task A_duplicate_sibling_name_is_rejected()
    {
        await Create.HandleAsync(new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", null, null),
                                 TestContext.Current.CancellationToken);

        var second = await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", null, null),
            TestContext.Current.CancellationToken);

        second.Error!.Code.Should().Be("account.duplicate_sibling_name");
    }

    [Fact]
    public async Task Renaming_an_account_rewrites_its_descendants_paths_in_the_database()
    {
        var gaming = (await Create.HandleAsync(
            new CreateAccountRequest("Gaming", "Expense", "Category", null, "EUR", null, null),
            TestContext.Current.CancellationToken)).Value;

        var steam = (await Create.HandleAsync(
            new CreateAccountRequest("Steam", "Expense", "Category", gaming.Id, "EUR", null, null),
            TestContext.Current.CancellationToken)).Value;

        var patch = new PatchAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);
        var result = await patch.HandleAsync(gaming.Id,
            new PatchAccountRequest("Games", null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Accounts.FindAsync(steam.Id))!.Path.Should().Be("/expense/games/steam");
    }

    [Fact]
    public async Task Archiving_an_account_hides_it_from_the_default_listing_but_keeps_it()
    {
        var cash = (await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", null, null),
            TestContext.Current.CancellationToken)).Value;

        await new ArchiveAccountHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(cash.Id, TestContext.Current.CancellationToken);

        var list = new ListAccountsHandler(_harness.Accounts);
        (await list.HandleAsync(null, null, includeArchived: false,
                                TestContext.Current.CancellationToken))
            .Should().NotContain(a => a.Id == cash.Id);
        (await list.HandleAsync(null, null, includeArchived: true,
                                TestContext.Current.CancellationToken))
            .Should().Contain(a => a.Id == cash.Id);
    }

    [Fact]
    public async Task An_account_balance_can_be_asked_for_as_of_a_date()
    {
        var cash = (await Create.HandleAsync(
            new CreateAccountRequest("Cash", "Asset", "Cash", null, "EUR", 100m, new DateOnly(2026, 6, 1)),
            TestContext.Current.CancellationToken)).Value;

        var handler = new GetAccountBalanceHandler(_harness.Accounts, _harness.Queries);

        (await handler.HandleAsync(cash.Id, new DateOnly(2026, 5, 1),
                                   TestContext.Current.CancellationToken)).Value.Balance
            .Should().Be(0m);
        (await handler.HandleAsync(cash.Id, new DateOnly(2026, 7, 1),
                                   TestContext.Current.CancellationToken)).Value.Balance
            .Should().Be(100m);
    }

    [Fact]
    public async Task Asking_for_the_balance_of_an_account_that_does_not_exist_is_a_not_found()
    {
        var handler = new GetAccountBalanceHandler(_harness.Accounts, _harness.Queries);

        (await handler.HandleAsync(Guid.NewGuid(), null, TestContext.Current.CancellationToken))
            .Error!.Code.Should().Be("account.not_found");
    }
}
```

If the xUnit version in use does not expose `TestContext.Current.CancellationToken`, pass `CancellationToken.None` instead — keep it explicit rather than relying on a default parameter, so a hung query in CI is diagnosable.

- [ ] **Step 3: Write the failing category use-case tests**

```csharp
using Money.Application.Categories;
using Money.Application.Contracts;

namespace Money.Application.Tests.Categories;

public sealed class CategoryUseCaseTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private CreateCategoryHandler Create => new(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);

    [Fact]
    public async Task A_category_is_an_expense_account_with_the_category_role()
    {
        var result = await Create.HandleAsync(new CreateCategoryRequest("Groceries", "Expense", null),
                                              TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be("Expense");
        result.Value.Role.Should().Be("Category");
        result.Value.Path.Should().Be("/expense/groceries");
    }

    [Fact]
    public async Task Categories_nest_and_come_back_as_a_tree()
    {
        var gaming = (await Create.HandleAsync(new CreateCategoryRequest("Gaming", "Expense", null),
                                               TestContext.Current.CancellationToken)).Value;
        await Create.HandleAsync(new CreateCategoryRequest("Steam", "Expense", gaming.Id),
                                 TestContext.Current.CancellationToken);
        await Create.HandleAsync(new CreateCategoryRequest("Food", "Expense", null),
                                 TestContext.Current.CancellationToken);

        var tree = await new GetCategoryTreeHandler(_harness.Accounts)
            .HandleAsync("Expense", includeArchived: false, TestContext.Current.CancellationToken);

        tree.Should().HaveCount(2);
        tree.Single(n => n.Name == "Gaming").Children.Should().ContainSingle(c => c.Name == "Steam");
        tree.Single(n => n.Name == "Food").Children.Should().BeEmpty();
    }

    [Fact]
    public async Task A_category_cannot_be_created_under_a_parent_of_the_other_kind()
    {
        var salary = (await Create.HandleAsync(new CreateCategoryRequest("Salary", "Income", null),
                                               TestContext.Current.CancellationToken)).Value;

        (await Create.HandleAsync(new CreateCategoryRequest("Groceries", "Expense", salary.Id),
                                  TestContext.Current.CancellationToken))
            .Error!.Code.Should().Be("account.parent_kind_mismatch");
    }

    [Fact]
    public async Task A_category_kind_other_than_income_or_expense_is_rejected()
    {
        (await Create.HandleAsync(new CreateCategoryRequest("Weird", "Asset", null),
                                  TestContext.Current.CancellationToken))
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task The_tree_is_ordered_by_sort_order_then_name()
    {
        await Create.HandleAsync(new CreateCategoryRequest("Zoo", "Expense", null),
                                 TestContext.Current.CancellationToken);
        await Create.HandleAsync(new CreateCategoryRequest("Apples", "Expense", null),
                                 TestContext.Current.CancellationToken);

        var tree = await new GetCategoryTreeHandler(_harness.Accounts)
            .HandleAsync("Expense", false, TestContext.Current.CancellationToken);

        tree.Select(n => n.Name).Should().ContainInOrder("Apples", "Zoo");
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter "AccountUseCaseTests|CategoryUseCaseTests"`
Expected: FAIL — the handlers do not exist.

- [ ] **Step 5: Implement `AccountMapper`**

```csharp
using Money.Application.Contracts;
using Money.Domain.Accounts;
using Money.Domain.Primitives;

namespace Money.Application.Mapping;

public static class AccountMapper
{
    public static AccountDto ToDto(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountDto(
            account.Id, account.Name, account.Kind.ToString(), account.Role.ToString(),
            account.ParentAccountId, account.Path, account.CurrencyCode, account.IsArchived,
            account.SortOrder, account.ColorHex, account.Icon, account.Notes);
    }

    public static Result<AccountKind> ParseKind(string? kind) =>
        Enum.TryParse<AccountKind>(kind, ignoreCase: true, out var parsed)
            ? Result<AccountKind>.Ok(parsed)
            : DomainErrors.Account.KindRoleMismatch(kind ?? "(none)", "(unknown)");

    public static Result<AccountRole> ParseRole(string? role) =>
        Enum.TryParse<AccountRole>(role, ignoreCase: true, out var parsed)
            ? Result<AccountRole>.Ok(parsed)
            : DomainErrors.Account.KindRoleMismatch("(unknown)", role ?? "(none)");
}
```

- [ ] **Step 6: Implement the account handlers**

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Accounts;

public sealed class CreateAccountHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<AccountDto>> HandleAsync(
        CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = AccountMapper.ParseKind(request.Kind);
        if (kind.IsFailure) return kind.Error!;

        var role = AccountMapper.ParseRole(request.Role);
        if (role.IsFailure) return role.Error!;

        var currency = Currency.FromCode(request.CurrencyCode ?? Currency.Eur.Code);
        if (currency.IsFailure) return currency.Error!;

        Account? parent = null;
        if (request.ParentAccountId is { } parentId)
        {
            parent = await accounts.FindAsync(parentId, cancellationToken);
            if (parent is null) return DomainErrors.Account.NotFound(parentId);
        }

        var siblings = await accounts.ChildrenOfAsync(request.ParentAccountId, cancellationToken);
        var slug = Account.Slugify(request.Name ?? "");
        if (siblings.Any(s => string.Equals(Account.Slugify(s.Name), slug, StringComparison.Ordinal)))
            return DomainErrors.Account.DuplicateSiblingName(request.Name ?? "");

        var now = clock.UtcNow;
        var created = Account.Create(
            Guid.CreateVersion7(now), request.Name ?? "", kind.Value, role.Value,
            parent, currency.Value, now);

        if (created.IsFailure) return created.Error!;

        var account = created.Value;
        account.UpdatePresentation(siblings.Count, null, null, null, request.OpenedOn, now);
        accounts.Add(account);

        if (request.OpeningBalance is { } opening && opening != 0m)
        {
            var equity = await EnsureOpeningBalanceAccountAsync(currency.Value, now, cancellationToken);
            if (equity.IsFailure) return equity.Error!;

            var amount = MoneyValue.Of(
                MoneyValue.RoundToMinor(opening * currency.Value.MinorUnitScale), currency.Value);

            var transaction = LedgerTemplates.OpeningBalance(
                Guid.CreateVersion7(now),
                request.OpenedOn ?? DateOnly.FromDateTime(now.UtcDateTime),
                account, equity.Value, amount, now);

            if (transaction.IsFailure) return transaction.Error!;

            transactions.Add(transaction.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AccountDto>.Ok(AccountMapper.ToDto(account));
    }

    private async Task<Result<Account>> EnsureOpeningBalanceAccountAsync(
        Currency currency, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await accounts.FindFirstByRoleAsync(AccountRole.OpeningBalance, cancellationToken);
        if (existing is not null) return Result<Account>.Ok(existing);

        var created = Account.Create(
            Guid.CreateVersion7(now), "Opening balance", AccountKind.Equity,
            AccountRole.OpeningBalance, null, currency, now);

        if (created.IsFailure) return created.Error!;

        accounts.Add(created.Value);
        return created;
    }
}
```

`DateOnly.FromDateTime(now.UtcDateTime)` here reads the *injected* clock, not ambient time, so the architecture rule is satisfied. It uses UTC rather than the configured zone because an opening balance with no explicit date is not period-sensitive; anywhere that *is* period-sensitive must go through `PeriodResolver.TodayIn`.

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Accounts;

public sealed class PatchAccountHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<AccountDto>> HandleAsync(
        Guid id, PatchAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await accounts.FindAsync(id, cancellationToken);
        if (account is null) return DomainErrors.Account.NotFound(id);

        var now = clock.UtcNow;

        if (request.Name is { } newName && !string.Equals(newName, account.Name, StringComparison.Ordinal))
        {
            var siblings = await accounts.ChildrenOfAsync(account.ParentAccountId, cancellationToken);
            var descendants = await accounts.DescendantsOfAsync(account.ChildPathPrefix, cancellationToken);

            var renamed = AccountTree.Rename(account, newName, siblings, descendants, now);
            if (renamed.IsFailure) return renamed.Error!;
        }

        if (request.ClearParent == true || request.ParentAccountId is not null)
        {
            Account? newParent = null;
            if (request.ClearParent != true && request.ParentAccountId is { } parentId)
            {
                newParent = await accounts.FindAsync(parentId, cancellationToken);
                if (newParent is null) return DomainErrors.Account.NotFound(parentId);
            }

            var newSiblings = await accounts.ChildrenOfAsync(newParent?.Id, cancellationToken);
            var descendants = await accounts.DescendantsOfAsync(account.ChildPathPrefix, cancellationToken);

            var moved = AccountTree.Move(account, newParent, newSiblings, descendants, now);
            if (moved.IsFailure) return moved.Error!;
        }

        account.UpdatePresentation(
            request.SortOrder ?? account.SortOrder,
            request.ColorHex ?? account.ColorHex,
            request.Icon ?? account.Icon,
            request.Notes ?? account.Notes,
            account.OpenedOn, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AccountDto>.Ok(AccountMapper.ToDto(account));
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Accounts;

public sealed class ArchiveAccountHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(id, cancellationToken);
        if (account is null) return Result.Fail(DomainErrors.Account.NotFound(id));

        var archived = account.Archive(clock.UtcNow);
        if (archived.IsFailure) return archived;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;

namespace Money.Application.Accounts;

public sealed class ListAccountsHandler(IAccountRepository accounts)
{
    public async Task<IReadOnlyList<AccountDto>> HandleAsync(
        string? kind, string? role, bool includeArchived, CancellationToken cancellationToken = default)
    {
        AccountKind? parsedKind = Enum.TryParse<AccountKind>(kind, true, out var k) ? k : null;
        AccountRole? parsedRole = Enum.TryParse<AccountRole>(role, true, out var r) ? r : null;

        var found = await accounts.ListAsync(parsedKind, parsedRole, includeArchived, cancellationToken);
        return found.Select(AccountMapper.ToDto).ToArray();
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Presentation;
using Money.Domain.Money;
using Money.Domain.Primitives;

namespace Money.Application.Accounts;

public sealed class GetAccountBalanceHandler(IAccountRepository accounts, ILedgerQueries queries)
{
    public async Task<Result<AccountBalanceDto>> HandleAsync(
        Guid id, DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(id, cancellationToken);
        if (account is null) return DomainErrors.Account.NotFound(id);

        var currency = Currency.FromCode(account.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var minor = await queries.BalanceOfAsync(id, asOf, cancellationToken);

        return Result<AccountBalanceDto>.Ok(new AccountBalanceDto(
            account.Id, account.Name,
            DisplayAmountMapper.ToDisplay(minor, account.Kind, currency.Value),
            account.CurrencyCode, asOf));
    }
}
```

- [ ] **Step 7: Implement the category handlers**

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Categories;

/// <summary>
/// Sugar over accounts. A category is an Income or Expense account with Role=Category; the UI
/// never says "account" about one.
/// </summary>
public sealed class CreateCategoryHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<AccountDto>> HandleAsync(
        CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = AccountMapper.ParseKind(request.Kind);
        if (kind.IsFailure) return kind.Error!;

        if (!Account.IsLegalCombination(kind.Value, AccountRole.Category))
            return DomainErrors.Account.KindRoleMismatch(kind.Value.ToString(), nameof(AccountRole.Category));

        Account? parent = null;
        if (request.ParentCategoryId is { } parentId)
        {
            parent = await accounts.FindAsync(parentId, cancellationToken);
            if (parent is null) return DomainErrors.Account.NotFound(parentId);
            if (!parent.IsCategory) return DomainErrors.Account.NotACategory(parent.Name);
        }

        var currency = parent is null
            ? Currency.Eur
            : Currency.FromCode(parent.CurrencyCode).Value;

        var siblings = await accounts.ChildrenOfAsync(request.ParentCategoryId, cancellationToken);
        var slug = Account.Slugify(request.Name ?? "");
        if (siblings.Any(s => string.Equals(Account.Slugify(s.Name), slug, StringComparison.Ordinal)))
            return DomainErrors.Account.DuplicateSiblingName(request.Name ?? "");

        var now = clock.UtcNow;
        var created = Account.Create(
            Guid.CreateVersion7(now), request.Name ?? "", kind.Value, AccountRole.Category,
            parent, currency, now);

        if (created.IsFailure) return created.Error!;

        created.Value.UpdatePresentation(siblings.Count, null, null, null, null, now);
        accounts.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<AccountDto>.Ok(AccountMapper.ToDto(created.Value));
    }
}
```

`Account.IsLegalCombination` is `public` (Task 10); `Account.ValidateName` stays `internal`. No visibility change is needed here.

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Domain.Accounts;

namespace Money.Application.Categories;

public sealed class GetCategoryTreeHandler(IAccountRepository accounts)
{
    public async Task<IReadOnlyList<CategoryNodeDto>> HandleAsync(
        string kind, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var parsedKind = Enum.TryParse<AccountKind>(kind, true, out var k) ? k : AccountKind.Expense;

        var all = await accounts.ListAsync(
            parsedKind, AccountRole.Category, includeArchived, cancellationToken);

        var byParent = all.GroupBy(a => a.ParentAccountId)
                          .ToDictionary(g => g.Key ?? Guid.Empty, g => g.ToList());

        return BuildChildren(Guid.Empty, byParent);
    }

    private static IReadOnlyList<CategoryNodeDto> BuildChildren(
        Guid parentKey, IReadOnlyDictionary<Guid, List<Account>> byParent)
    {
        if (!byParent.TryGetValue(parentKey, out var children)) return [];

        return children
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name, StringComparer.CurrentCulture)
            .Select(a => new CategoryNodeDto(
                a.Id, a.Name, a.Path, a.Kind.ToString(), a.IsArchived, a.ColorHex, a.Icon,
                BuildChildren(a.Id, byParent)))
            .ToArray();
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Application.Tests --filter "AccountUseCaseTests|CategoryUseCaseTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Money.Application src/Money.Domain tests/Money.Application.Tests
git commit -m "feat: add account and category use cases over real SQLite"
```

---

### Task 22: Transaction use cases

**Files:**
- Create: `src/Money.Application/Transactions/CreateTransactionHandler.cs`, `GetTransactionHandler.cs`, `ReplaceTransactionHandler.cs`, `VoidTransactionHandler.cs`, `ListTransactionsHandler.cs`
- Create: `src/Money.Application/Mapping/TransactionMapper.cs`
- Test: `tests/Money.Application.Tests/Transactions/TransactionUseCaseTests.cs`

**Interfaces:**
- Consumes: `ITransactionRepository`, `IAccountRepository`, `ILedgerQueries`, `IUnitOfWork`, `IClock`, `Transaction`, `DisplayAmountMapper`.
- Produces:
  - `sealed class CreateTransactionHandler(IAccountRepository, ITransactionRepository, IUnitOfWork, IClock)` — `Task<Result<TransactionDto>> HandleAsync(CreateTransactionRequest, CancellationToken)`
  - `sealed class GetTransactionHandler(ITransactionRepository, IAccountRepository)` — `Task<Result<TransactionDto>> HandleAsync(Guid, CancellationToken)`
  - `sealed class ReplaceTransactionHandler(IAccountRepository, ITransactionRepository, IUnitOfWork, IClock)` — `Task<Result<TransactionDto>> HandleAsync(Guid, CreateTransactionRequest, CancellationToken)`
  - `sealed class VoidTransactionHandler(ITransactionRepository, IUnitOfWork, IClock)` — `Task<Result> HandleAsync(Guid, string reason, CancellationToken)`
  - `sealed class ListTransactionsHandler(ILedgerQueries)` — `Task<TransactionPageDto> HandleAsync(TransactionQuery, CancellationToken)`
  - `static class TransactionMapper` — `static TransactionDto ToDto(Transaction, IReadOnlyDictionary<Guid, Account>)`, `static TransactionPageDto ToPageDto(TransactionPage, IReadOnlyDictionary<Guid, string>)`

**Request amounts are display amounts.** A line's `Amount` is what the user typed for that account, so it goes through `DisplayAmountMapper.ToStored` with that account's kind. This is the only conversion point on the write path.

- [ ] **Step 1: Write the failing tests**

```csharp
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;

namespace Money.Application.Tests.Transactions;

public sealed class TransactionUseCaseTests : IAsyncLifetime, IDisposable
{
    private readonly UseCaseHarness _harness = new();
    private AccountDto _bank = null!;
    private AccountDto _food = null!;
    private AccountDto _salary = null!;
    private AccountDto _savings = null!;

    public async ValueTask InitializeAsync()
    {
        var accounts = new CreateAccountHandler(_harness.Accounts, _harness.Transactions,
                                                _harness.UnitOfWork, _harness.Clock);
        var categories = new CreateCategoryHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);

        _bank = (await accounts.HandleAsync(
            new CreateAccountRequest("Current", "Asset", "Bank", null, "EUR", 1000m,
                                     new DateOnly(2026, 1, 1)), CancellationToken.None)).Value;
        _savings = (await accounts.HandleAsync(
            new CreateAccountRequest("Rainy day", "Asset", "SavingsPocket", null, "EUR", null, null),
            CancellationToken.None)).Value;
        _food = (await categories.HandleAsync(
            new CreateCategoryRequest("Food", "Expense", null), CancellationToken.None)).Value;
        _salary = (await categories.HandleAsync(
            new CreateCategoryRequest("Salary", "Income", null), CancellationToken.None)).Value;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() => _harness.Dispose();

    private CreateTransactionHandler Create => new(_harness.Accounts, _harness.Transactions,
                                                   _harness.UnitOfWork, _harness.Clock);

    [Fact]
    public async Task A_balanced_expense_is_saved_and_moves_the_balance()
    {
        var result = await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", "Trattoria",
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(98_000);
        (await _harness.Queries.BalanceOfAsync(_food.Id, null)).Should().Be(2_000);
    }

    [Fact]
    public async Task An_income_line_typed_as_a_positive_number_is_stored_as_a_credit()
    {
        // The user types 3000 next to "Salary". Internally that must become -300000.
        await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "September salary", null,
            [
                new TransactionLineRequest(_bank.Id, 3000m, null),
                new TransactionLineRequest(_salary.Id, 3000m, null)
            ]), CancellationToken.None);

        (await _harness.Queries.BalanceOfAsync(_salary.Id, null)).Should().Be(-300_000);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(400_000);
    }

    [Fact]
    public async Task An_unbalanced_request_is_rejected_and_nothing_is_saved()
    {
        var result = await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Broken", null,
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -19.00m, null)
            ]), CancellationToken.None);

        result.Error!.Code.Should().Be("transaction.does_not_balance");
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(100_000);
    }

    [Fact]
    public async Task A_transaction_can_be_read_back_with_its_lines_named_and_oriented()
    {
        var created = (await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", null,
            [
                new TransactionLineRequest(_food.Id, 20.00m, "Pizza"),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None)).Value;

        var fetched = (await new GetTransactionHandler(_harness.Transactions, _harness.Accounts)
            .HandleAsync(created.Id, CancellationToken.None)).Value;

        fetched.Lines.Should().HaveCount(2);
        fetched.Lines.Single(l => l.AccountId == _food.Id).Amount.Should().Be(20.00m);
        fetched.Lines.Single(l => l.AccountId == _food.Id).AccountName.Should().Be("Food");
        fetched.Lines.Single(l => l.AccountId == _food.Id).Memo.Should().Be("Pizza");
        fetched.Lines.Single(l => l.AccountId == _bank.Id).Amount.Should().Be(-20.00m);
    }

    [Fact]
    public async Task Replacing_a_transaction_changes_the_balances_accordingly()
    {
        var created = (await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", null,
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None)).Value;

        var result = await new ReplaceTransactionHandler(
                _harness.Accounts, _harness.Transactions, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(created.Id, new CreateTransactionRequest(
                new DateOnly(2026, 9, 2), "Dinner (corrected)", null,
                [
                    new TransactionLineRequest(_food.Id, 25.00m, null),
                    new TransactionLineRequest(_bank.Id, -25.00m, null)
                ]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_food.Id, null)).Should().Be(2_500);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(97_500);
    }

    [Fact]
    public async Task Voiding_a_transaction_removes_it_from_every_balance_but_keeps_the_row()
    {
        var created = (await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", null,
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None)).Value;

        var result = await new VoidTransactionHandler(
                _harness.Transactions, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(created.Id, "Entered twice", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(100_000);
        (await _harness.Transactions.FindAsync(created.Id))!.IsVoided.Should().BeTrue();
    }

    [Fact]
    public async Task Voiding_a_transaction_that_does_not_exist_is_a_not_found()
    {
        (await new VoidTransactionHandler(_harness.Transactions, _harness.UnitOfWork, _harness.Clock)
            .HandleAsync(Guid.NewGuid(), "Nope", CancellationToken.None))
            .Error!.Code.Should().Be("transaction.not_found");
    }

    [Fact]
    public async Task Moving_money_into_savings_does_not_appear_in_the_expense_total()
    {
        await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "To savings", null,
            [
                new TransactionLineRequest(_savings.Id, 500.00m, null),
                new TransactionLineRequest(_bank.Id, -500.00m, null)
            ]), CancellationToken.None);

        (await _harness.Queries.SubtreeBalanceAsync("/expense", null, null)).Should().Be(0);
        (await _harness.Queries.BalanceOfAsync(_savings.Id, null)).Should().Be(50_000);
    }

    [Fact]
    public async Task The_list_shows_a_transaction_with_its_category_and_account_names()
    {
        await Create.HandleAsync(new CreateTransactionRequest(
            new DateOnly(2026, 9, 1), "Dinner", "Trattoria",
            [
                new TransactionLineRequest(_food.Id, 20.00m, null),
                new TransactionLineRequest(_bank.Id, -20.00m, null)
            ]), CancellationToken.None);

        var page = await new ListTransactionsHandler(_harness.Queries).HandleAsync(
            new TransactionQuery(null, null, null, null, null, false, null, 20), CancellationToken.None);

        var row = page.Items.Single(i => i.Description == "Dinner");
        row.CategoryName.Should().Be("Food");
        row.AccountName.Should().Be("Current");
        row.Amount.Should().Be(20.00m);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter TransactionUseCaseTests`
Expected: FAIL — the handlers do not exist.

- [ ] **Step 3: Implement `TransactionMapper`**

```csharp
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
```

- [ ] **Step 4: Implement the handlers**

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Transactions;

public sealed class CreateTransactionHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        CreateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = await TransactionLineResolver.ResolveAsync(
            accounts, request.Lines, cancellationToken);
        if (resolved.IsFailure) return resolved.Error!;

        var now = clock.UtcNow;
        var created = Transaction.Create(
            Guid.CreateVersion7(now), request.OccurredOn, request.Description ?? "", request.Payee,
            TransactionSourceKind.Manual, sourceId: null,
            resolved.Value.Drafts, resolved.Value.AccountsById, now);

        if (created.IsFailure) return created.Error!;

        transactions.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<TransactionDto>.Ok(
            TransactionMapper.ToDto(created.Value, resolved.Value.AccountsById));
    }
}

/// <summary>
/// Turns display amounts into stored minor units, using each line's own account kind. This is the
/// only sign conversion on the write path (see DisplayAmountMapper).
/// </summary>
internal static class TransactionLineResolver
{
    internal sealed record Resolved(
        IReadOnlyList<PostingDraft> Drafts, IReadOnlyDictionary<Guid, Account> AccountsById);

    public static async Task<Result<Resolved>> ResolveAsync(
        IAccountRepository accounts,
        IReadOnlyList<TransactionLineRequest> lines,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
            return DomainErrors.Transaction.TooFewPostings(0);

        var accountsById = new Dictionary<Guid, Account>();
        var drafts = new List<PostingDraft>(lines.Count);

        foreach (var line in lines)
        {
            if (!accountsById.TryGetValue(line.AccountId, out var account))
            {
                account = await accounts.FindAsync(line.AccountId, cancellationToken);
                if (account is null) return DomainErrors.Transaction.AccountUnknown(line.AccountId);
                accountsById[line.AccountId] = account;
            }

            var currency = Currency.FromCode(account.CurrencyCode);
            if (currency.IsFailure) return currency.Error!;

            var minor = DisplayAmountMapper.ToStored(line.Amount, account.Kind, currency.Value);
            drafts.Add(new PostingDraft(line.AccountId, MoneyValue.Of(minor, currency.Value), line.Memo));
        }

        return Result<Resolved>.Ok(new Resolved(drafts, accountsById));
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Primitives;

namespace Money.Application.Transactions;

public sealed class GetTransactionHandler(
    ITransactionRepository transactions, IAccountRepository accounts)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await transactions.FindAsync(id, cancellationToken);
        if (transaction is null) return DomainErrors.Transaction.NotFound(id);

        var accountsById = new Dictionary<Guid, Account>();
        foreach (var posting in transaction.Postings)
        {
            if (accountsById.ContainsKey(posting.AccountId)) continue;

            var account = await accounts.FindAsync(posting.AccountId, cancellationToken);
            if (account is null) return DomainErrors.Transaction.AccountUnknown(posting.AccountId);
            accountsById[posting.AccountId] = account;
        }

        return Result<TransactionDto>.Ok(TransactionMapper.ToDto(transaction, accountsById));
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Transactions;

public sealed class ReplaceTransactionHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        Guid id, CreateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var transaction = await transactions.FindAsync(id, cancellationToken);
        if (transaction is null) return DomainErrors.Transaction.NotFound(id);

        var resolved = await TransactionLineResolver.ResolveAsync(
            accounts, request.Lines, cancellationToken);
        if (resolved.IsFailure) return resolved.Error!;

        var replaced = transaction.Replace(
            request.OccurredOn, request.Description ?? "", request.Payee,
            resolved.Value.Drafts, resolved.Value.AccountsById, clock.UtcNow);

        if (replaced.IsFailure) return replaced.Error!;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<TransactionDto>.Ok(
            TransactionMapper.ToDto(transaction, resolved.Value.AccountsById));
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Transactions;

public sealed class VoidTransactionHandler(
    ITransactionRepository transactions, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(
        Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var transaction = await transactions.FindAsync(id, cancellationToken);
        if (transaction is null) return Result.Fail(DomainErrors.Transaction.NotFound(id));

        var voided = transaction.Void(reason, clock.UtcNow);
        if (voided.IsFailure) return voided;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;

namespace Money.Application.Transactions;

public sealed class ListTransactionsHandler(ILedgerQueries queries)
{
    public async Task<TransactionPageDto> HandleAsync(
        TransactionQuery query, CancellationToken cancellationToken = default) =>
        TransactionMapper.ToPageDto(await queries.ListAsync(query, cancellationToken));
}
```

Note that `Replace` mutates a tracked entity whose old postings must be deleted. EF's cascade on the owned collection handles this because `_postings` is the backing field of a configured collection navigation: clearing it marks the removed postings as deleted. Verify this in the replace test — if orphaned postings survive, add `context.Postings.RemoveRange(...)` inside a repository method rather than reaching into EF from the application layer.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Application.Tests --filter TransactionUseCaseTests`
Expected: PASS, 9 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Money.Application tests/Money.Application.Tests
git commit -m "feat: add transaction create, read, replace, void and list use cases"
```

---

### Task 23: Quick-expense and transfer sugar

**Files:**
- Create: `src/Money.Application/Transactions/QuickExpenseHandler.cs`, `TransferHandler.cs`
- Test: `tests/Money.Application.Tests/Transactions/SugarUseCaseTests.cs`

**Interfaces:**
- Consumes: `LedgerTemplates`, `IAccountRepository`, `ITransactionRepository`, `IUnitOfWork`, `IClock`, `ISettingsRepository`, `PeriodResolver`.
- Produces:
  - `sealed class QuickExpenseHandler(IAccountRepository, ITransactionRepository, ISettingsRepository, IUnitOfWork, IClock)` — `Task<Result<TransactionDto>> HandleAsync(QuickExpenseRequest, CancellationToken)`
  - `sealed class TransferHandler(IAccountRepository, ITransactionRepository, ISettingsRepository, IUnitOfWork, IClock)` — `Task<Result<TransactionDto>> HandleAsync(TransferRequest, CancellationToken)`

These two back the quick-add control, which success criterion 1 ("recording a normal expense takes under five seconds") depends on. When `OccurredOn` is omitted they default to **today in the configured time zone**, resolved through `PeriodResolver.TodayIn` — never `DateTime.Today`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;

namespace Money.Application.Tests.Transactions;

public sealed class SugarUseCaseTests : IAsyncLifetime, IDisposable
{
    private readonly UseCaseHarness _harness = new();
    private AccountDto _bank = null!;
    private AccountDto _savings = null!;
    private AccountDto _food = null!;

    public async ValueTask InitializeAsync()
    {
        var accounts = new CreateAccountHandler(_harness.Accounts, _harness.Transactions,
                                                _harness.UnitOfWork, _harness.Clock);
        var categories = new CreateCategoryHandler(_harness.Accounts, _harness.UnitOfWork, _harness.Clock);

        _bank = (await accounts.HandleAsync(new CreateAccountRequest(
            "Current", "Asset", "Bank", null, "EUR", 1000m, new DateOnly(2026, 1, 1)),
            CancellationToken.None)).Value;
        _savings = (await accounts.HandleAsync(new CreateAccountRequest(
            "Rainy day", "Asset", "SavingsPocket", null, "EUR", null, null),
            CancellationToken.None)).Value;
        _food = (await categories.HandleAsync(new CreateCategoryRequest("Food", "Expense", null),
                                              CancellationToken.None)).Value;

        await _harness.Settings.SaveAsync(new Abstractions.AppSettings(
            "EUR", Domain.Periods.PeriodDefinition.Default, 10, true), CancellationToken.None);
        await _harness.UnitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() => _harness.Dispose();

    private QuickExpenseHandler QuickExpense => new(
        _harness.Accounts, _harness.Transactions, _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    private TransferHandler Transfer => new(
        _harness.Accounts, _harness.Transactions, _harness.Settings, _harness.UnitOfWork, _harness.Clock);

    [Fact]
    public async Task A_quick_expense_needs_only_an_amount_a_category_and_an_account()
    {
        var result = await QuickExpense.HandleAsync(
            new QuickExpenseRequest(12.50m, _food.Id, _bank.Id, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_food.Id, null)).Should().Be(1_250);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(98_750);
    }

    [Fact]
    public async Task A_quick_expense_without_a_date_uses_today_in_the_configured_time_zone()
    {
        // 23:30 UTC on 31 August is already 1 September in Budapest.
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 8, 31, 23, 30, 0, TimeSpan.Zero);

        var result = await QuickExpense.HandleAsync(
            new QuickExpenseRequest(5m, _food.Id, _bank.Id, null, null, null), CancellationToken.None);

        result.Value.OccurredOn.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task A_quick_expense_without_a_description_is_named_after_its_category()
    {
        var result = await QuickExpense.HandleAsync(
            new QuickExpenseRequest(5m, _food.Id, _bank.Id, null, null, null), CancellationToken.None);

        result.Value.Description.Should().Be("Food");
    }

    [Fact]
    public async Task A_quick_expense_against_a_non_category_is_rejected()
    {
        (await QuickExpense.HandleAsync(
            new QuickExpenseRequest(5m, _savings.Id, _bank.Id, null, null, null), CancellationToken.None))
            .Error!.Code.Should().Be("account.not_a_category");
    }

    [Fact]
    public async Task A_quick_expense_of_zero_or_less_is_rejected()
    {
        (await QuickExpense.HandleAsync(
            new QuickExpenseRequest(0m, _food.Id, _bank.Id, null, null, null), CancellationToken.None))
            .Error!.Code.Should().Be("transaction.zero_amount");
    }

    [Fact]
    public async Task A_transfer_moves_money_and_is_invisible_to_spending_reports()
    {
        var result = await Transfer.HandleAsync(
            new TransferRequest(200m, _bank.Id, _savings.Id, new DateOnly(2026, 9, 1), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _harness.Queries.BalanceOfAsync(_savings.Id, null)).Should().Be(20_000);
        (await _harness.Queries.BalanceOfAsync(_bank.Id, null)).Should().Be(80_000);
        (await _harness.Queries.SubtreeBalanceAsync("/expense", null, null)).Should().Be(0);
    }

    [Fact]
    public async Task A_transfer_into_a_category_is_rejected()
    {
        (await Transfer.HandleAsync(
            new TransferRequest(10m, _bank.Id, _food.Id, null, null), CancellationToken.None))
            .Error!.Code.Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_is_rejected()
    {
        (await Transfer.HandleAsync(
            new TransferRequest(10m, _bank.Id, _bank.Id, null, null), CancellationToken.None))
            .Error!.Code.Should().Be("transaction.duplicate_account");
    }

    [Fact]
    public async Task A_transfer_is_described_in_plain_language_by_default()
    {
        var result = await Transfer.HandleAsync(
            new TransferRequest(200m, _bank.Id, _savings.Id, null, null), CancellationToken.None);

        result.Value.Description.Should().Be("Current to Rainy day");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter SugarUseCaseTests`
Expected: FAIL — the handlers do not exist.

- [ ] **Step 3: Implement a shared "today" helper**

Create `src/Money.Application/Abstractions/TodayResolver.cs`:

```csharp
using Money.Domain.Periods;
using Money.Domain.Time;

namespace Money.Application.Abstractions;

/// <summary>
/// Today's date in the user's configured time zone. The only correct answer to "what is today"
/// anywhere in the application layer - PeriodResolver owns the conversion (spec 5.4).
/// </summary>
public static class TodayResolver
{
    public static async Task<DateOnly> TodayAsync(
        ISettingsRepository settings, IClock clock, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync(cancellationToken);
        var definition = stored?.PeriodDefinition ?? PeriodDefinition.Default;
        return new PeriodResolver(definition).TodayIn(clock);
    }
}
```

- [ ] **Step 4: Implement `QuickExpenseHandler`**

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Transactions;

public sealed class QuickExpenseHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        QuickExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = await accounts.FindAsync(request.CategoryId, cancellationToken);
        if (category is null) return DomainErrors.Account.NotFound(request.CategoryId);

        var paidFrom = await accounts.FindAsync(request.AccountId, cancellationToken);
        if (paidFrom is null) return DomainErrors.Account.NotFound(request.AccountId);

        var currency = Currency.FromCode(paidFrom.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var amount = MoneyValue.Of(
            MoneyValue.RoundToMinor(request.Amount * currency.Value.MinorUnitScale), currency.Value);

        var occurredOn = request.OccurredOn
            ?? await TodayResolver.TodayAsync(settings, clock, cancellationToken);

        var now = clock.UtcNow;
        var created = LedgerTemplates.Expense(
            Guid.CreateVersion7(now), occurredOn,
            string.IsNullOrWhiteSpace(request.Description) ? category.Name : request.Description.Trim(),
            request.Payee, paidFrom, category, amount, now);

        if (created.IsFailure) return created.Error!;

        transactions.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var byId = new Dictionary<Guid, Account> { [category.Id] = category, [paidFrom.Id] = paidFrom };
        return Result<TransactionDto>.Ok(TransactionMapper.ToDto(created.Value, byId));
    }
}
```

- [ ] **Step 5: Implement `TransferHandler`**

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.Transactions;

public sealed class TransferHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ISettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<TransactionDto>> HandleAsync(
        TransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var from = await accounts.FindAsync(request.FromAccountId, cancellationToken);
        if (from is null) return DomainErrors.Account.NotFound(request.FromAccountId);

        var to = await accounts.FindAsync(request.ToAccountId, cancellationToken);
        if (to is null) return DomainErrors.Account.NotFound(request.ToAccountId);

        if (from.Id == to.Id) return DomainErrors.Transaction.DuplicateAccount(from.Name);

        var currency = Currency.FromCode(from.CurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var amount = MoneyValue.Of(
            MoneyValue.RoundToMinor(request.Amount * currency.Value.MinorUnitScale), currency.Value);

        var occurredOn = request.OccurredOn
            ?? await TodayResolver.TodayAsync(settings, clock, cancellationToken);

        var now = clock.UtcNow;
        var created = LedgerTemplates.Transfer(
            Guid.CreateVersion7(now), occurredOn,
            string.IsNullOrWhiteSpace(request.Description)
                ? $"{from.Name} to {to.Name}"
                : request.Description.Trim(),
            from, to, amount, now);

        if (created.IsFailure) return created.Error!;

        transactions.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var byId = new Dictionary<Guid, Account> { [from.Id] = from, [to.Id] = to };
        return Result<TransactionDto>.Ok(TransactionMapper.ToDto(created.Value, byId));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Application.Tests --filter SugarUseCaseTests`
Expected: PASS, 9 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Money.Application tests/Money.Application.Tests
git commit -m "feat: add quick-expense and transfer use cases with time-zone-correct defaults"
```

---

### Task 24: Settings and the first-run setup use case

**Files:**
- Create: `src/Money.Application/Settings/GetSettingsHandler.cs`, `UpdateSettingsHandler.cs`
- Create: `src/Money.Application/FirstRun/CompleteFirstRunSetupHandler.cs`, `StarterCategories.cs`
- Test: `tests/Money.Application.Tests/Settings/SettingsUseCaseTests.cs`, `tests/Money.Application.Tests/FirstRun/FirstRunTests.cs`

**Interfaces:**
- Produces:
  - `sealed class GetSettingsHandler(ISettingsRepository)` — `Task<SettingsDto> HandleAsync(CancellationToken)`
  - `sealed class UpdateSettingsHandler(ISettingsRepository, IUnitOfWork)` — `Task<Result<SettingsDto>> HandleAsync(UpdateSettingsRequest, CancellationToken)`
  - `sealed class CompleteFirstRunSetupHandler(ISettingsRepository, IAccountRepository, ITransactionRepository, IUnitOfWork, IClock)` — `Task<Result<SettingsDto>> HandleAsync(FirstRunRequest, CancellationToken)`
  - `static class StarterCategories` — `static IReadOnlyList<string> Expense` = Housing, Groceries, Eating out, Alcohol, Gaming, Transport, Health, Subscriptions, Other; `static IReadOnlyList<string> Income` = Salary, Other income (spec section 10)

- [ ] **Step 1: Write the failing tests**

```csharp
using Money.Application.Contracts;
using Money.Application.Settings;

namespace Money.Application.Tests.Settings;

public sealed class SettingsUseCaseTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Before_first_run_the_defaults_are_reported()
    {
        var settings = await new GetSettingsHandler(_harness.Settings)
            .HandleAsync(CancellationToken.None);

        settings.BaseCurrencyCode.Should().Be("EUR");
        settings.FirstRunCompleted.Should().BeFalse();
        settings.PeriodAnchor.Should().Be("CalendarMonth");
        settings.PeriodAnchorDay.Should().Be(1);
    }

    [Fact]
    public async Task Settings_round_trip_through_the_database()
    {
        var update = new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork);

        var saved = await update.HandleAsync(new UpdateSettingsRequest(
            "HUF", "DayOfMonth", 25, "Europe/Budapest", "Sunday", 5), CancellationToken.None);

        saved.IsSuccess.Should().BeTrue();

        var read = await new GetSettingsHandler(_harness.Settings).HandleAsync(CancellationToken.None);
        read.BaseCurrencyCode.Should().Be("HUF");
        read.PeriodAnchor.Should().Be("DayOfMonth");
        read.PeriodAnchorDay.Should().Be(25);
        read.FirstDayOfWeek.Should().Be("Sunday");
        read.BackupRetentionCount.Should().Be(5);
    }

    [Fact]
    public async Task An_anchor_day_above_twenty_eight_is_rejected_with_the_explanation()
    {
        var result = await new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new UpdateSettingsRequest("EUR", "DayOfMonth", 31, "Europe/Budapest",
                                                   "Monday", 10), CancellationToken.None);

        result.Error!.Code.Should().Be("period.anchor_day_out_of_range");
        result.Error.Message.Should().Contain("not exist in every month");
    }

    [Fact]
    public async Task An_unknown_time_zone_is_rejected()
    {
        (await new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new UpdateSettingsRequest("EUR", "CalendarMonth", 1, "Mars/Olympus",
                                                   "Monday", 10), CancellationToken.None))
            .Error!.Code.Should().Be("period.unknown_time_zone");
    }

    [Fact]
    public async Task An_unknown_currency_is_rejected()
    {
        (await new UpdateSettingsHandler(_harness.Settings, _harness.UnitOfWork)
            .HandleAsync(new UpdateSettingsRequest("XYZ", "CalendarMonth", 1, "Europe/Budapest",
                                                   "Monday", 10), CancellationToken.None))
            .Error!.Code.Should().Be("currency.unknown");
    }
}
```

```csharp
using Money.Application.Contracts;
using Money.Application.FirstRun;
using Money.Application.Settings;

namespace Money.Application.Tests.FirstRun;

public sealed class FirstRunTests : IDisposable
{
    private readonly UseCaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private CompleteFirstRunSetupHandler Handler => new(
        _harness.Settings, _harness.Accounts, _harness.Transactions,
        _harness.UnitOfWork, _harness.Clock);

    private static FirstRunRequest ARequest(bool seed = true) => new(
        "EUR", "DayOfMonth", 25, "Europe/Budapest", "Monday",
        "Erste Current", "Bank", 1500.00m, new DateOnly(2026, 1, 1), seed);

    [Fact]
    public async Task First_run_stores_the_settings_marks_itself_complete_and_creates_the_account()
    {
        var result = await Handler.HandleAsync(ARequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FirstRunCompleted.Should().BeTrue();

        var account = await _harness.Accounts.FindByPathAsync("/asset/erste-current");
        account.Should().NotBeNull();
        (await _harness.Queries.BalanceOfAsync(account!.Id, null)).Should().Be(150_000);
    }

    [Fact]
    public async Task First_run_seeds_the_starter_categories()
    {
        await Handler.HandleAsync(ARequest(), CancellationToken.None);

        var expenses = await _harness.Accounts.ListAsync(
            Domain.Accounts.AccountKind.Expense, Domain.Accounts.AccountRole.Category, false);

        expenses.Select(a => a.Name).Should().Contain(
            ["Housing", "Groceries", "Eating out", "Alcohol", "Gaming",
             "Transport", "Health", "Subscriptions", "Other"]);

        var income = await _harness.Accounts.ListAsync(
            Domain.Accounts.AccountKind.Income, Domain.Accounts.AccountRole.Category, false);
        income.Select(a => a.Name).Should().Contain("Salary");
    }

    [Fact]
    public async Task Seeding_can_be_declined()
    {
        await Handler.HandleAsync(ARequest(seed: false), CancellationToken.None);

        (await _harness.Accounts.ListAsync(
            Domain.Accounts.AccountKind.Expense, Domain.Accounts.AccountRole.Category, false))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task The_opening_balance_is_not_spending_and_the_books_balance()
    {
        await Handler.HandleAsync(ARequest(), CancellationToken.None);

        (await _harness.Queries.SubtreeBalanceAsync("/expense", null, null)).Should().Be(0);
        (await _harness.Queries.AllBalancesAsync()).Sum(b => b.BalanceMinor).Should().Be(0);
    }

    [Fact]
    public async Task Running_first_run_twice_is_rejected()
    {
        await Handler.HandleAsync(ARequest(), CancellationToken.None);

        (await Handler.HandleAsync(ARequest(), CancellationToken.None))
            .Error!.Code.Should().Be("settings.already_initialised");
    }

    [Fact]
    public async Task A_bad_anchor_day_aborts_first_run_without_creating_anything()
    {
        var bad = ARequest() with { PeriodAnchorDay = 31 };

        var result = await Handler.HandleAsync(bad, CancellationToken.None);

        result.Error!.Code.Should().Be("period.anchor_day_out_of_range");
        (await _harness.Accounts.ListAllAsync()).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter "SettingsUseCaseTests|FirstRunTests"`
Expected: FAIL — the handlers do not exist.

- [ ] **Step 3: Implement a settings mapper and the two settings handlers**

Add to `src/Money.Application/Mapping/SettingsMapper.cs`:

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Domain.Money;
using Money.Domain.Periods;
using Money.Domain.Primitives;

namespace Money.Application.Mapping;

public static class SettingsMapper
{
    public const string CalendarMonthAnchorName = "CalendarMonth";
    public const string DayOfMonthAnchorName = "DayOfMonth";

    public static SettingsDto ToDto(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var anchor = settings.PeriodDefinition.Anchor is PeriodAnchor.DayOfMonthAnchor
            ? DayOfMonthAnchorName
            : CalendarMonthAnchorName;

        return new SettingsDto(
            settings.BaseCurrencyCode, anchor, settings.PeriodDefinition.Anchor.AnchorDay,
            settings.PeriodDefinition.TimeZoneId, settings.PeriodDefinition.FirstDayOfWeek.ToString(),
            settings.BackupRetentionCount, settings.FirstRunCompleted);
    }

    public static Result<PeriodDefinition> BuildDefinition(
        string? anchorName, int anchorDay, string? timeZoneId, string? firstDayOfWeek)
    {
        Result<PeriodAnchor> anchor;

        if (string.Equals(anchorName, DayOfMonthAnchorName, StringComparison.OrdinalIgnoreCase))
        {
            anchor = PeriodAnchor.DayOfMonth(anchorDay);
            if (anchor.IsFailure) return anchor.Error!;
        }
        else
        {
            anchor = Result<PeriodAnchor>.Ok(PeriodAnchor.CalendarMonth);
        }

        if (!Enum.TryParse<DayOfWeek>(firstDayOfWeek, ignoreCase: true, out var day))
            day = DayOfWeek.Monday;

        return PeriodDefinition.Create(anchor.Value, timeZoneId ?? "", day);
    }

    public static Result<string> ValidateCurrency(string? code) =>
        Currency.FromCode(code ?? "").Map(c => c.Code);
}
```

These two constants hold the same values as `SettingsRepository`'s (Task 20) and `SettingsEntity`'s default (Task 18): `"CalendarMonth"` and `"DayOfMonth"`. The `SettingsUseCaseTests` round-trip is what proves they agree — if it fails, one of the three drifted.

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Periods;

namespace Money.Application.Settings;

public sealed class GetSettingsHandler(ISettingsRepository settings)
{
    public async Task<SettingsDto> HandleAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetAsync(cancellationToken)
            ?? new AppSettings("EUR", PeriodDefinition.Default, 10, false);

        return SettingsMapper.ToDto(stored);
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Periods;
using Money.Domain.Primitives;

namespace Money.Application.Settings;

public sealed class UpdateSettingsHandler(ISettingsRepository settings, IUnitOfWork unitOfWork)
{
    public async Task<Result<SettingsDto>> HandleAsync(
        UpdateSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currency = SettingsMapper.ValidateCurrency(request.BaseCurrencyCode);
        if (currency.IsFailure) return currency.Error!;

        var definition = SettingsMapper.BuildDefinition(
            request.PeriodAnchor, request.PeriodAnchorDay, request.TimeZoneId, request.FirstDayOfWeek);
        if (definition.IsFailure) return definition.Error!;

        var existing = await settings.GetAsync(cancellationToken);

        var updated = new AppSettings(
            currency.Value, definition.Value,
            Math.Clamp(request.BackupRetentionCount, 1, 100),
            existing?.FirstRunCompleted ?? false);

        await settings.SaveAsync(updated, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SettingsDto>.Ok(SettingsMapper.ToDto(updated));
    }
}
```

- [ ] **Step 4: Implement the starter categories and first-run handler**

```csharp
namespace Money.Application.FirstRun;

/// <summary>The starter tree from spec section 10. The user renames or deletes freely.</summary>
public static class StarterCategories
{
    public static IReadOnlyList<string> Expense { get; } =
    [
        "Housing", "Groceries", "Eating out", "Alcohol", "Gaming",
        "Transport", "Health", "Subscriptions", "Other"
    ];

    public static IReadOnlyList<string> Income { get; } = ["Salary", "Other income"];
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Mapping;
using Money.Domain.Accounts;
using Money.Domain.Ledger;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;
using MoneyValue = Money.Domain.Money.Money;

namespace Money.Application.FirstRun;

/// <summary>
/// The whole of first run in one transaction: settings, the first account with its opening
/// balance, and the starter category tree. Everything validates before anything is written, so a
/// rejected anchor day leaves an empty database rather than a half-built one.
/// </summary>
public sealed class CompleteFirstRunSetupHandler(
    ISettingsRepository settings,
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<SettingsDto>> HandleAsync(
        FirstRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await settings.GetAsync(cancellationToken);
        if (existing?.FirstRunCompleted == true) return DomainErrors.Settings.AlreadyInitialised();

        var currencyCode = SettingsMapper.ValidateCurrency(request.BaseCurrencyCode);
        if (currencyCode.IsFailure) return currencyCode.Error!;

        var definition = SettingsMapper.BuildDefinition(
            request.PeriodAnchor, request.PeriodAnchorDay, request.TimeZoneId, request.FirstDayOfWeek);
        if (definition.IsFailure) return definition.Error!;

        var role = AccountMapper.ParseRole(request.FirstAccountRole);
        if (role.IsFailure) return role.Error!;
        if (role.Value is not (AccountRole.Bank or AccountRole.Cash))
            return DomainErrors.Account.KindRoleMismatch(nameof(AccountKind.Asset), request.FirstAccountRole);

        var currency = Currency.FromCode(currencyCode.Value).Value;
        var now = clock.UtcNow;

        var firstAccount = Account.Create(
            Guid.CreateVersion7(now), request.FirstAccountName, AccountKind.Asset, role.Value,
            null, currency, now);
        if (firstAccount.IsFailure) return firstAccount.Error!;

        var openingEquity = Account.Create(
            Guid.CreateVersion7(now), "Opening balance", AccountKind.Equity,
            AccountRole.OpeningBalance, null, currency, now);
        if (openingEquity.IsFailure) return openingEquity.Error!;

        Transaction? opening = null;
        if (request.OpeningBalance != 0m)
        {
            var amount = MoneyValue.Of(
                MoneyValue.RoundToMinor(request.OpeningBalance * currency.MinorUnitScale), currency);

            var built = LedgerTemplates.OpeningBalance(
                Guid.CreateVersion7(now), request.OpenedOn,
                firstAccount.Value, openingEquity.Value, amount, now);

            if (built.IsFailure) return built.Error!;
            opening = built.Value;
        }

        // Nothing has been written yet. From here on, every step is known to succeed.
        firstAccount.Value.UpdatePresentation(0, null, null, null, request.OpenedOn, now);
        accounts.Add(firstAccount.Value);
        accounts.Add(openingEquity.Value);
        if (opening is not null) transactions.Add(opening);

        if (request.SeedStarterCategories)
        {
            SeedCategories(StarterCategories.Expense, AccountKind.Expense, currency, now);
            SeedCategories(StarterCategories.Income, AccountKind.Income, currency, now);
        }

        var saved = new AppSettings(currencyCode.Value, definition.Value, 10, FirstRunCompleted: true);
        await settings.SaveAsync(saved, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SettingsDto>.Ok(SettingsMapper.ToDto(saved));
    }

    private void SeedCategories(
        IReadOnlyList<string> names, AccountKind kind, Currency currency, DateTimeOffset now)
    {
        for (var i = 0; i < names.Count; i++)
        {
            var created = Account.Create(
                Guid.CreateVersion7(now), names[i], kind, AccountRole.Category, null, currency, now);

            if (created.IsFailure) continue;

            created.Value.UpdatePresentation(i, null, null, null, null, now);
            accounts.Add(created.Value);
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Application.Tests --filter "SettingsUseCaseTests|FirstRunTests"`
Expected: PASS, 11 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Money.Application tests/Money.Application.Tests
git commit -m "feat: add settings use cases and the first-run setup that seeds the starter tree"
```

---

### Task 25: Backup, export and the admin use cases

**Files:**
- Create: `src/Money.Infrastructure/Backup/SqliteBackupService.cs`
- Create: `src/Money.Infrastructure/Export/JsonExportService.cs`, `CsvExportService.cs`
- Create: `src/Money.Infrastructure/Identity/LocalCurrentUser.cs`
- Create: `src/Money.Application/Admin/CreateBackupHandler.cs`, `ExportLedgerHandler.cs`, `RunIntegrityCheckHandler.cs`
- Test: `tests/Money.Application.Tests/Admin/BackupAndExportTests.cs`

**Interfaces:**
- Produces:
  - `sealed class SqliteBackupService(MoneyDbContext, BackupOptions, IClock)` implementing `IBackupService`; `sealed record BackupOptions(string DataDirectory, int RetentionCount)`
  - `sealed class JsonExportService(MoneyDbContext)` and `sealed class CsvExportService(MoneyDbContext)` implementing `IExportService`
  - `sealed class LocalCurrentUser : ICurrentUser` returning `"Local user"`
  - `sealed class CreateBackupHandler(IBackupService)` — `Task<Result<BackupResultDto>> HandleAsync(CancellationToken)`
  - `sealed class ExportLedgerHandler(Func<string, IExportService?> resolve)` — `Task<Result<(string Content, string ContentType, string FileName)>> HandleAsync(string? format, CancellationToken)`
  - `sealed class RunIntegrityCheckHandler(IIntegrityChecker)` — `Task<IntegrityReportDto> HandleAsync(CancellationToken)`

Two classes implement `IExportService`, so the handler takes a **resolver function** rather than the services themselves: `Func<string, IExportService?>`. The alternative — keyed services with `[FromKeyedServices]` — would put an ASP.NET Core attribute in `Money.Application`, which the dependency-rule architecture test forbids. The factory is supplied at composition time in Task 26.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using Money.Application.Abstractions;
using Money.Infrastructure.Backup;
using Money.Infrastructure.Export;
using Money.TestSupport;

namespace Money.Application.Tests.Admin;

public sealed class BackupAndExportTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "money-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public async Task A_backup_writes_a_timestamped_file_into_the_backups_folder()
    {
        using var context = _fixture.NewContext();
        await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var service = new SqliteBackupService(
            context, new BackupOptions(_tempDirectory, RetentionCount: 10), FakeClock.At(2026, 9, 1, 14, 30));

        var path = await service.CreateBackupAsync();

        File.Exists(path).Should().BeTrue();
        Path.GetFileName(path).Should().Be("money-20260901-143000.db");
        new FileInfo(path).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Old_backups_beyond_the_retention_count_are_removed()
    {
        using var context = _fixture.NewContext();
        var clock = FakeClock.At(2026, 9, 1, 0, 0);
        var service = new SqliteBackupService(
            context, new BackupOptions(_tempDirectory, RetentionCount: 3), clock);

        for (var i = 0; i < 5; i++)
        {
            await service.CreateBackupAsync();
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        (await service.ListBackupsAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task A_backup_is_a_readable_database_containing_the_same_rows()
    {
        using var context = _fixture.NewContext();
        var ledger = await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var service = new SqliteBackupService(
            context, new BackupOptions(_tempDirectory, 10), FakeClock.At(2026, 9, 1, 14, 30));
        var path = await service.CreateBackupAsync();

        using var restored = new Microsoft.Data.Sqlite.SqliteConnection($"DataSource={path}");
        restored.Open();
        using var command = restored.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Transactions";

        Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(ledger.Transactions.Count);
    }

    [Fact]
    public async Task The_json_export_contains_accounts_and_transactions_and_re_parses()
    {
        using var context = _fixture.NewContext();
        var ledger = await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var json = await new JsonExportService(context).ExportJsonAsync();

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("accounts").GetArrayLength()
            .Should().Be(ledger.Accounts.Count);
        document.RootElement.GetProperty("transactions").GetArrayLength()
            .Should().Be(ledger.Transactions.Count);
        document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task The_csv_export_is_a_flat_entry_table_with_a_header()
    {
        using var context = _fixture.NewContext();
        var ledger = await LedgerSeeder.SeedAsync(context, LedgerGen.Ledgers.Single());

        var csv = await new CsvExportService(context).ExportCsvAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be(
            "TransactionId,OccurredOn,Description,Payee,IsVoided,AccountPath,AccountName," +
            "AmountMinor,CurrencyCode,Memo");
        lines.Should().HaveCount(1 + ledger.Transactions.Sum(t => t.Postings.Count));
    }

    [Fact]
    public async Task A_field_containing_a_comma_or_a_quote_is_escaped_in_the_csv()
    {
        using var context = _fixture.NewContext();
        var ledger = LedgerGen.Ledgers.Single();
        await LedgerSeeder.SeedAsync(context, ledger);

        using (var command = _fixture.Connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE Transactions SET Description = 'Dinner, with \"friends\"' " +
                $"WHERE Id = '{ledger.Transactions[0].Id}'";
            command.ExecuteNonQuery();
        }

        var csv = await new CsvExportService(context).ExportCsvAsync();

        csv.Should().Contain("\"Dinner, with \"\"friends\"\"\"");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Application.Tests --filter BackupAndExportTests`
Expected: FAIL — the services do not exist.

- [ ] **Step 3: Implement `SqliteBackupService`**

```csharp
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Domain.Time;
using Money.Infrastructure.Persistence;

namespace Money.Infrastructure.Backup;

public sealed record BackupOptions(string DataDirectory, int RetentionCount);

/// <summary>
/// Uses SQLite's Online Backup API, so a backup is consistent even while the app is running.
/// Copying the file with File.Copy would not be safe in WAL mode.
/// </summary>
public sealed class SqliteBackupService(MoneyDbContext context, BackupOptions options, IClock clock)
    : IBackupService
{
    public Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var backupDirectory = DataDirectory.BackupDirectoryIn(options.DataDirectory);
        Directory.CreateDirectory(backupDirectory);

        var fileName = "money-"
            + clock.UtcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + ".db";
        var path = Path.Combine(backupDirectory, fileName);

        var source = (SqliteConnection)context.Database.GetDbConnection();
        var wasClosed = source.State != System.Data.ConnectionState.Open;
        if (wasClosed) source.Open();

        try
        {
            using var destination = new SqliteConnection($"DataSource={path}");
            destination.Open();
            source.BackupDatabase(destination);
        }
        finally
        {
            if (wasClosed) source.Close();
        }

        ApplyRetention(backupDirectory);
        return Task.FromResult(path);
    }

    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var backupDirectory = DataDirectory.BackupDirectoryIn(options.DataDirectory);
        if (!Directory.Exists(backupDirectory))
            return Task.FromResult<IReadOnlyList<BackupInfo>>([]);

        IReadOnlyList<BackupInfo> backups = Directory.EnumerateFiles(backupDirectory, "money-*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupInfo(f.Name, new DateTimeOffset(f.CreationTimeUtc, TimeSpan.Zero), f.Length))
            .ToArray();

        return Task.FromResult(backups);
    }

    private void ApplyRetention(string backupDirectory)
    {
        var keep = Math.Max(1, options.RetentionCount);

        var stale = Directory.EnumerateFiles(backupDirectory, "money-*.db")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(keep)
            .ToArray();

        foreach (var path in stale) File.Delete(path);
    }
}
```

Retention orders by **file name**, not creation time: the name carries a sortable timestamp, and file-system timestamps can be rewritten by sync clients or restores.

- [ ] **Step 4: Implement the two export services**

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Infrastructure.Persistence;

namespace Money.Infrastructure.Export;

/// <summary>
/// A documented, re-importable shape (spec section 8). schemaVersion is bumped whenever the
/// shape changes so a future import path can tell what it is reading.
/// </summary>
public sealed class JsonExportService(MoneyDbContext context) : IExportService
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<string> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await context.Accounts.OrderBy(a => a.Path)
            .Select(a => new
            {
                a.Id, a.Name, Kind = a.Kind.ToString(), Role = a.Role.ToString(),
                a.ParentAccountId, a.Path, a.CurrencyCode, a.IsArchived,
                a.OpenedOn, a.SortOrder, a.ColorHex, a.Icon, a.Notes
            })
            .ToListAsync(cancellationToken);

        var transactions = await context.Transactions
            .OrderBy(t => t.OccurredOn).ThenBy(t => t.Id)
            .Select(t => new
            {
                t.Id, t.OccurredOn, t.BookedAtUtc, t.Description, t.Payee,
                SourceKind = t.SourceKind.ToString(), t.SourceId, t.ExternalRef,
                t.IsVoided, t.VoidedAtUtc, t.VoidReason,
                Entries = t.Postings.Select(p => new
                {
                    p.Id, p.AccountId, p.AmountMinor, p.CurrencyCode, p.Memo
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var settings = await context.Settings.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            accounts,
            transactions,
            settings
        }, Options);
    }

    public Task<string> ExportCsvAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Use CsvExportService for CSV.");
}
```

```csharp
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Infrastructure.Persistence;

namespace Money.Infrastructure.Export;

/// <summary>A flat entry table for spreadsheet use (spec section 8).</summary>
public sealed class CsvExportService(MoneyDbContext context) : IExportService
{
    public Task<string> ExportJsonAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Use JsonExportService for JSON.");

    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Postings
            .Join(context.Transactions, p => p.TransactionId, t => t.Id, (p, t) => new { p, t })
            .Join(context.Accounts, x => x.p.AccountId, a => a.Id, (x, a) => new
            {
                x.t.Id,
                x.t.OccurredOn,
                x.t.Description,
                x.t.Payee,
                x.t.IsVoided,
                a.Path,
                AccountName = a.Name,
                x.p.AmountMinor,
                x.p.CurrencyCode,
                x.p.Memo
            })
            .OrderBy(r => r.OccurredOn).ThenBy(r => r.Id).ThenBy(r => r.Path)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.Append("TransactionId,OccurredOn,Description,Payee,IsVoided,AccountPath,AccountName,")
               .Append("AmountMinor,CurrencyCode,Memo\n");

        foreach (var row in rows)
        {
            builder
                .Append(Escape(row.Id.ToString())).Append(',')
                .Append(Escape(row.OccurredOn.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(row.Description)).Append(',')
                .Append(Escape(row.Payee)).Append(',')
                .Append(row.IsVoided ? "true" : "false").Append(',')
                .Append(Escape(row.Path)).Append(',')
                .Append(Escape(row.AccountName)).Append(',')
                .Append(row.AmountMinor.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.CurrencyCode)).Append(',')
                .Append(Escape(row.Memo)).Append('\n');
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var needsQuotes = value.IndexOfAny([',', '"', '\n', '\r']) >= 0;
        if (!needsQuotes) return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
```

Amounts are exported as **minor units**, not as decimals: a spreadsheet that reads `20.00` as a float is exactly the failure mode spec D8 exists to prevent. Document this in the export screen's help text.

- [ ] **Step 5: Implement `LocalCurrentUser` and the three admin handlers**

```csharp
using Money.Application.Abstractions;

namespace Money.Infrastructure.Identity;

/// <summary>Desktop mode has one user and no login. Server mode (phase 9) replaces this.</summary>
public sealed class LocalCurrentUser : ICurrentUser
{
    public string DisplayName => "Local user";
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Domain.Primitives;

namespace Money.Application.Admin;

public sealed class CreateBackupHandler(IBackupService backups)
{
    public async Task<Result<BackupResultDto>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = await backups.CreateBackupAsync(cancellationToken);
            var info = (await backups.ListBackupsAsync(cancellationToken))
                .FirstOrDefault(b => b.FileName == Path.GetFileName(path));

            return info is null
                ? DomainErrors.Admin.BackupFailed("the backup file could not be found after writing")
                : Result<BackupResultDto>.Ok(
                    new BackupResultDto(info.FileName, info.CreatedAtUtc, info.SizeBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DomainErrors.Admin.BackupFailed(ex.Message);
        }
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Domain.Primitives;

namespace Money.Application.Admin;

public sealed class ExportLedgerHandler(Func<string, IExportService?> resolve)
{
    public async Task<Result<(string Content, string ContentType, string FileName)>> HandleAsync(
        string? format, CancellationToken cancellationToken = default)
    {
        var normalised = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToLowerInvariant();

        var service = resolve(normalised);
        if (service is null) return DomainErrors.Admin.UnsupportedExportFormat(format ?? "");

        return normalised switch
        {
            "json" => Result<(string, string, string)>.Ok((
                await service.ExportJsonAsync(cancellationToken),
                "application/json", "money-export.json")),
            "csv" => Result<(string, string, string)>.Ok((
                await service.ExportCsvAsync(cancellationToken),
                "text/csv", "money-export.csv")),
            _ => DomainErrors.Admin.UnsupportedExportFormat(format ?? "")
        };
    }
}
```

```csharp
using Money.Application.Abstractions;
using Money.Application.Contracts;

namespace Money.Application.Admin;

public sealed class RunIntegrityCheckHandler(IIntegrityChecker checker)
{
    public async Task<IntegrityReportDto> HandleAsync(CancellationToken cancellationToken = default)
    {
        var report = await checker.CheckAsync(cancellationToken);
        return new IntegrityReportDto(
            report.IsHealthy, report.Findings.Select(f => $"{f.Check}: {f.Detail}").ToArray());
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Application.Tests --filter BackupAndExportTests`
Expected: PASS, 6 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Money.Infrastructure src/Money.Application tests/Money.Application.Tests
git commit -m "feat: add SQLite online backup with retention, JSON and CSV export, admin handlers"
```

---

### Task 26: API composition root, Problem Details and the test host

**Files:**
- Create: `src/Money.Api/Program.cs`, `src/Money.Api/HostingMode.cs`, `src/Money.Api/Infrastructure/DomainErrorResults.cs`, `src/Money.Api/DependencyInjection.cs`
- Create: `src/Money.Api/appsettings.json`
- Test: `tests/Money.Api.Tests/ApiFactory.cs`, `tests/Money.Api.Tests/HealthAndProblemDetailsTests.cs`

**Interfaces:**
- Produces:
  - `enum HostingMode { Desktop = 1, Server = 2 }`
  - `static class DomainErrorResults` — `static IResult ToProblem(DomainError error)`, `static int StatusCodeFor(string code)`
  - `static class DependencyInjection` — `static IServiceCollection AddMoneyApp(this IServiceCollection, IConfiguration, HostingMode)`
  - `public partial class Program` (so `WebApplicationFactory<Program>` can find it)
  - `sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` in the test project, with `FakeClock Clock` and `HttpClient CreateApiClient()`

**Status-code mapping, decided once:**

| Code suffix / pattern | Status |
|---|---|
| `*.not_found` | 404 |
| `*.already_voided`, `*.already_initialised`, `*.already_archived`, `*.duplicate_*` | 409 |
| everything else | 400 |

`type` is `https://moneyapp.local/problems/{code}`, `title` is a short human phrase, `detail` is `error.Message`, and an extension member `code` carries the raw error code so a client can branch without parsing URLs.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Money.Api.Tests;

public sealed class HealthAndProblemDetailsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthAndProblemDetailsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_reports_ok()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_openapi_document_is_served_at_the_versioned_path()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("/api/v1/transactions");
    }

    [Fact]
    public async Task A_missing_account_produces_an_rfc_9457_problem_document()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/v1/accounts/{Guid.NewGuid()}/balance", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        document.RootElement.GetProperty("code").GetString().Should().Be("account.not_found");
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://moneyapp.local/problems/account.not_found");
    }

    [Fact]
    public async Task A_validation_failure_is_a_four_hundred()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bad", kind = "Asset", role = "Category", currencyCode = "EUR" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("code").GetString().Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task A_conflict_is_a_four_hundred_and_nine()
    {
        using var client = _factory.CreateApiClient();

        var create = new { name = "Cash", kind = "Asset", role = "Cash", currencyCode = "EUR" };
        await client.PostAsJsonAsync("/api/v1/accounts", create, TestContext.Current.CancellationToken);
        var second = await client.PostAsJsonAsync("/api/v1/accounts", create,
                                                  TestContext.Current.CancellationToken);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
```

- [ ] **Step 2: Write the API test factory**

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Money.Domain.Time;
using Money.Infrastructure.Persistence;
using Money.TestSupport;

namespace Money.Api.Tests;

/// <summary>
/// The whole app over a real, private SQLite database held in memory for the lifetime of the
/// factory, with a FakeClock so no test reads the system clock.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:;Foreign Keys=True");

    public FakeClock Clock { get; } = FakeClock.At(2026, 9, 1, 9, 0);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            _connection.Open();

            services.RemoveAll<DbContextOptions<MoneyDbContext>>();
            services.RemoveAll<MoneyDbContext>();
            services.AddDbContext<MoneyDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<MoneyDbContext>().Database.Migrate();
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
```

Every API test class that needs a genuinely empty database constructs its own `ApiFactory`; classes that share one via `IClassFixture` must use random names so they cannot collide.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Api.Tests`
Expected: FAIL — `Program` is not accessible and the endpoints do not exist.

- [ ] **Step 4: Implement `HostingMode` and `DomainErrorResults`**

```csharp
namespace Money.Api;

/// <summary>
/// Read once at startup. Everything below the API layer is identical in both shapes, so moving
/// to server mode (phase 9) is a registration change, not a rewrite (spec section 11).
/// </summary>
public enum HostingMode
{
    Desktop = 1,
    Server = 2
}
```

```csharp
using Microsoft.AspNetCore.Http;
using Money.Domain.Primitives;

namespace Money.Api.Infrastructure;

/// <summary>Maps domain errors onto RFC 9457 Problem Details. One place, one table.</summary>
public static class DomainErrorResults
{
    private const string TypeBase = "https://moneyapp.local/problems/";

    public static int StatusCodeFor(string code)
    {
        if (code.EndsWith(".not_found", StringComparison.Ordinal)) return StatusCodes.Status404NotFound;

        if (code.EndsWith(".already_voided", StringComparison.Ordinal)
            || code.EndsWith(".already_initialised", StringComparison.Ordinal)
            || code.EndsWith(".already_archived", StringComparison.Ordinal)
            || code.Contains(".duplicate_", StringComparison.Ordinal))
        {
            return StatusCodes.Status409Conflict;
        }

        return StatusCodes.Status400BadRequest;
    }

    public static IResult ToProblem(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusCodeFor(error.Code);

        return Results.Problem(
            detail: error.Message,
            statusCode: status,
            title: TitleFor(status),
            type: TypeBase + error.Code,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static IResult ToProblem<T>(Result<T> result) => ToProblem(result.Error!);

    public static IResult ToProblem(Result result) => ToProblem(result.Error!);

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        _ => "Request could not be completed"
    };
}
```

- [ ] **Step 5: Implement `DependencyInjection`**

```csharp
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Admin;
using Money.Application.Categories;
using Money.Application.FirstRun;
using Money.Application.Settings;
using Money.Application.Transactions;
using Money.Domain.Time;
using Money.Infrastructure.Backup;
using Money.Infrastructure.Export;
using Money.Infrastructure.Identity;
using Money.Infrastructure.Persistence;
using Money.Infrastructure.Persistence.Repositories;
using Money.Infrastructure.Time;

namespace Money.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddMoneyApp(
        this IServiceCollection services, IConfiguration configuration, HostingMode mode)
    {
        var dataDirectory = DataDirectory.Resolve(
            Environment.GetEnvironmentVariable(DataDirectory.EnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        Directory.CreateDirectory(dataDirectory);

        var connectionString = DataDirectory.ConnectionStringFor(
            DataDirectory.DatabasePathIn(dataDirectory));

        services.AddDbContext<MoneyDbContext>(options =>
            options.UseSqlite(connectionString).AddInterceptors(new SqlitePragmaInterceptor()));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICurrentUser, LocalCurrentUser>();
        services.AddSingleton(mode);

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<ILedgerQueries, LedgerQueries>();
        services.AddScoped<IIntegrityChecker, IntegrityChecker>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddScoped<IBackupService>(provider => new SqliteBackupService(
            provider.GetRequiredService<MoneyDbContext>(),
            new BackupOptions(dataDirectory, RetentionCount: 10),
            provider.GetRequiredService<IClock>()));

        services.AddScoped<JsonExportService>();
        services.AddScoped<CsvExportService>();
        services.AddScoped(provider => new ExportLedgerHandler(format => format switch
        {
            "json" => provider.GetRequiredService<JsonExportService>(),
            "csv" => provider.GetRequiredService<CsvExportService>(),
            _ => null
        }));

        services.AddScoped<DatabaseInitializer>();

        services.AddScoped<CreateAccountHandler>();
        services.AddScoped<PatchAccountHandler>();
        services.AddScoped<ArchiveAccountHandler>();
        services.AddScoped<ListAccountsHandler>();
        services.AddScoped<GetAccountBalanceHandler>();
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<GetCategoryTreeHandler>();
        services.AddScoped<CreateTransactionHandler>();
        services.AddScoped<GetTransactionHandler>();
        services.AddScoped<ReplaceTransactionHandler>();
        services.AddScoped<VoidTransactionHandler>();
        services.AddScoped<ListTransactionsHandler>();
        services.AddScoped<QuickExpenseHandler>();
        services.AddScoped<TransferHandler>();
        services.AddScoped<GetSettingsHandler>();
        services.AddScoped<UpdateSettingsHandler>();
        services.AddScoped<CompleteFirstRunSetupHandler>();
        services.AddScoped<CreateBackupHandler>();
        services.AddScoped<RunIntegrityCheckHandler>();

        return services;
    }
}
```

- [ ] **Step 6: Implement `Program.cs`**

```csharp
using Money.Api;
using Money.Api.Endpoints;
using Money.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var mode = args.Contains("--server", StringComparer.OrdinalIgnoreCase)
    ? HostingMode.Server
    : HostingMode.Desktop;

builder.Services.AddMoneyApp(builder.Configuration, mode);
builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");

var app = builder.Build();

// Migrations run after an automatic pre-migration backup (spec section 8).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitialiseAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseStaticFiles();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

var api = app.MapGroup("/api/v1");
api.MapAccountEndpoints();
api.MapCategoryEndpoints();
api.MapTransactionEndpoints();
api.MapSettingsEndpoints();
api.MapAdminEndpoints();

app.MapRazorPages();

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can host the app in tests.</summary>
public partial class Program;
```

`Program.cs` is on the ambient-time allow-list, but it does not actually read the clock — keep it that way.

- [ ] **Step 7: Write `appsettings.json`**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/Money.Api.Tests --filter HealthAndProblemDetailsTests`
Expected: the health and OpenAPI tests PASS; the three account tests still FAIL because Task 27 has not added the endpoints. Leave them red and move on — that is the next task's starting condition.

- [ ] **Step 9: Commit**

```bash
git add src/Money.Api tests/Money.Api.Tests
git commit -m "feat: add API composition root, hosting mode, Problem Details mapping and test host"
```

---

### Task 27: Account, category and settings endpoints

**Files:**
- Create: `src/Money.Api/Endpoints/AccountEndpoints.cs`, `CategoryEndpoints.cs`, `SettingsEndpoints.cs`
- Test: `tests/Money.Api.Tests/AccountEndpointTests.cs`, `CategoryEndpointTests.cs`, `SettingsEndpointTests.cs`

**Interfaces:**
- Produces extension methods on `RouteGroupBuilder`: `MapAccountEndpoints()`, `MapCategoryEndpoints()`, `MapSettingsEndpoints()`, each returning the group.
- Routes (spec section 9): `GET/POST /accounts`, `PATCH /accounts/{id}`, `POST /accounts/{id}/archive`, `GET /accounts/{id}/balance`, `GET/POST /categories`, `GET/PUT /settings`, `POST /settings/first-run`.

- [ ] **Step 1: Write the failing endpoint tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class AccountEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AccountEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task An_account_can_be_created_listed_and_read_back()
    {
        using var client = _factory.CreateApiClient();
        var name = "Erste " + Guid.NewGuid().ToString("N")[..6];

        var created = await client.PostAsJsonAsync("/api/v1/accounts",
            new { name, kind = "Asset", role = "Bank", currencyCode = "EUR", openingBalance = 1000m,
                  openedOn = "2026-01-01" },
            TestContext.Current.CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location.Should().NotBeNull();

        var dto = await created.Content.ReadFromJsonAsync<AccountDto>(
            TestContext.Current.CancellationToken);
        dto!.Path.Should().StartWith("/asset/erste-");

        var listed = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts?kind=Asset", TestContext.Current.CancellationToken);
        listed!.Should().Contain(a => a.Id == dto.Id);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{dto.Id}/balance", TestContext.Current.CancellationToken);
        balance!.Balance.Should().Be(1000m);
    }

    [Fact]
    public async Task An_account_can_be_renamed_and_archived()
    {
        using var client = _factory.CreateApiClient();
        var name = "Cash " + Guid.NewGuid().ToString("N")[..6];

        var dto = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name, kind = "Asset", role = "Cash", currencyCode = "EUR" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var patched = await client.PatchAsJsonAsync($"/api/v1/accounts/{dto!.Id}",
            new { name = name + " renamed" }, TestContext.Current.CancellationToken);
        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        var archived = await client.PostAsync($"/api/v1/accounts/{dto.Id}/archive", null,
                                              TestContext.Current.CancellationToken);
        archived.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listed = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts", TestContext.Current.CancellationToken);
        listed!.Should().NotContain(a => a.Id == dto.Id);

        var withArchived = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts?includeArchived=true", TestContext.Current.CancellationToken);
        withArchived!.Should().Contain(a => a.Id == dto.Id);
    }

    [Fact]
    public async Task A_balance_can_be_asked_for_as_of_a_date()
    {
        using var client = _factory.CreateApiClient();
        var name = "Wallet " + Guid.NewGuid().ToString("N")[..6];

        var dto = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name, kind = "Asset", role = "Cash", currencyCode = "EUR",
                  openingBalance = 50m, openedOn = "2026-06-01" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var before = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{dto!.Id}/balance?asOf=2026-05-01", TestContext.Current.CancellationToken);

        before!.Balance.Should().Be(0m);
    }
}
```

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class CategoryEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CategoryEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Categories_can_be_created_and_come_back_as_a_tree()
    {
        using var client = _factory.CreateApiClient();
        var parentName = "Gaming " + Guid.NewGuid().ToString("N")[..6];

        var parent = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = parentName, kind = "Expense" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var child = await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Steam", kind = "Expense", parentCategoryId = parent!.Id },
            TestContext.Current.CancellationToken);
        child.StatusCode.Should().Be(HttpStatusCode.Created);

        var tree = await client.GetFromJsonAsync<List<CategoryNodeDto>>(
            "/api/v1/categories?kind=Expense", TestContext.Current.CancellationToken);

        tree!.Single(n => n.Id == parent.Id).Children.Should().ContainSingle(c => c.Name == "Steam");
    }

    [Fact]
    public async Task The_api_never_uses_accounting_vocabulary_for_categories()
    {
        using var client = _factory.CreateApiClient();

        var body = await client.GetStringAsync("/api/v1/categories?kind=Expense",
                                               TestContext.Current.CancellationToken);

        body.Should().NotContain("posting", "the ledger's vocabulary must not leak into the API");
        body.Should().NotContain("debit");
    }
}
```

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class SettingsEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SettingsEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Settings_can_be_read_and_written()
    {
        using var client = _factory.CreateApiClient();

        var updated = await client.PutAsJsonAsync("/api/v1/settings",
            new { baseCurrencyCode = "EUR", periodAnchor = "DayOfMonth", periodAnchorDay = 25,
                  timeZoneId = "Europe/Budapest", firstDayOfWeek = "Monday", backupRetentionCount = 7 },
            TestContext.Current.CancellationToken);

        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await client.GetFromJsonAsync<SettingsDto>(
            "/api/v1/settings", TestContext.Current.CancellationToken);

        read!.PeriodAnchorDay.Should().Be(25);
        read.BackupRetentionCount.Should().Be(7);
    }

    [Fact]
    public async Task An_anchor_day_above_twenty_eight_is_a_four_hundred_with_the_explanation()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync("/api/v1/settings",
            new { baseCurrencyCode = "EUR", periodAnchor = "DayOfMonth", periodAnchorDay = 31,
                  timeZoneId = "Europe/Budapest", firstDayOfWeek = "Monday", backupRetentionCount = 10 },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("not exist in every month");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Api.Tests`
Expected: FAIL — the endpoints are not mapped.

- [ ] **Step 3: Implement `AccountEndpoints`**

```csharp
using Money.Api.Infrastructure;
using Money.Application.Accounts;
using Money.Application.Contracts;

namespace Money.Api.Endpoints;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder group)
    {
        var accounts = group.MapGroup("/accounts").WithTags("Accounts");

        accounts.MapGet("/", async (
            string? kind, string? role, bool? includeArchived,
            ListAccountsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(
                kind, role, includeArchived ?? false, cancellationToken)))
            .WithName("ListAccounts");

        accounts.MapPost("/", async (
            CreateAccountRequest request, CreateAccountHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/accounts/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        }).WithName("CreateAccount");

        accounts.MapPatch("/{id:guid}", async (
            Guid id, PatchAccountRequest request, PatchAccountHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("PatchAccount");

        accounts.MapPost("/{id:guid}/archive", async (
            Guid id, ArchiveAccountHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : DomainErrorResults.ToProblem(result);
        }).WithName("ArchiveAccount");

        accounts.MapGet("/{id:guid}/balance", async (
            Guid id, DateOnly? asOf, GetAccountBalanceHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, asOf, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("GetAccountBalance");

        return group;
    }
}
```

- [ ] **Step 4: Implement `CategoryEndpoints` and `SettingsEndpoints`**

```csharp
using Money.Api.Infrastructure;
using Money.Application.Categories;
using Money.Application.Contracts;

namespace Money.Api.Endpoints;

public static class CategoryEndpoints
{
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder group)
    {
        var categories = group.MapGroup("/categories").WithTags("Categories");

        categories.MapGet("/", async (
            string? kind, bool? includeArchived, GetCategoryTreeHandler handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(
                kind ?? "Expense", includeArchived ?? false, cancellationToken)))
            .WithName("GetCategoryTree");

        categories.MapPost("/", async (
            CreateCategoryRequest request, CreateCategoryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/accounts/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        }).WithName("CreateCategory");

        return group;
    }
}
```

```csharp
using Money.Api.Infrastructure;
using Money.Application.Contracts;
using Money.Application.FirstRun;
using Money.Application.Settings;

namespace Money.Api.Endpoints;

public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder group)
    {
        var settings = group.MapGroup("/settings").WithTags("Settings");

        settings.MapGet("/", async (GetSettingsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("GetSettings");

        settings.MapPut("/", async (
            UpdateSettingsRequest request, UpdateSettingsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("UpdateSettings");

        settings.MapPost("/first-run", async (
            FirstRunRequest request, CompleteFirstRunSetupHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("CompleteFirstRun");

        return group;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Api.Tests`
Expected: PASS for the account, category, settings and Problem Details tests.

`AccountEndpointTests` and `SettingsEndpointTests` share one `ApiFactory` (and therefore one database) across the class. The tests above use random names precisely so they do not collide. Keep that discipline for every new API test.

- [ ] **Step 6: Commit**

```bash
git add src/Money.Api tests/Money.Api.Tests
git commit -m "feat: add account, category and settings endpoints"
```

---

### Task 28: Transaction endpoints with `Idempotency-Key`

**Files:**
- Create: `src/Money.Api/Endpoints/TransactionEndpoints.cs`, `src/Money.Api/Infrastructure/IdempotencyFilter.cs`
- Test: `tests/Money.Api.Tests/TransactionEndpointTests.cs`, `IdempotencyTests.cs`

**Interfaces:**
- Produces: `MapTransactionEndpoints()` covering `GET /transactions`, `POST /transactions`, `GET /transactions/{id}`, `PUT /transactions/{id}`, `POST /transactions/{id}/void`, `POST /transactions/quick-expense`, `POST /transactions/transfer`.
- Produces: `sealed class IdempotencyFilter(IIdempotencyStore, IUnitOfWork, IClock) : IEndpointFilter`.

The filter is applied to the three POSTs that create money movements. Semantics: if `Idempotency-Key` is absent the request proceeds normally; if present and already recorded, the stored response body is replayed with `200 OK` and an `Idempotency-Replayed: true` header; otherwise the request runs and a successful response body is recorded under the key.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class TransactionEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TransactionEndpointTests(ApiFactory factory) => _factory = factory;

    private async Task<(Guid Bank, Guid Category)> SeedAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 1000m, openedOn = "2026-01-01" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Food " + suffix, kind = "Expense" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        return (bank!.Id, category!.Id);
    }

    [Fact]
    public async Task A_quick_expense_can_be_posted_and_appears_in_the_list()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        var created = await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 12.50m, categoryId = category, accountId = bank,
                  occurredOn = "2026-09-01", description = "Lunch" },
            TestContext.Current.CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await client.GetFromJsonAsync<TransactionPageDto>(
            $"/api/v1/transactions?accountId={bank}", TestContext.Current.CancellationToken);

        page!.Items.Should().Contain(i => i.Description == "Lunch" && i.Amount == 12.50m);
    }

    [Fact]
    public async Task A_transfer_can_be_posted_and_is_absent_from_spending()
    {
        using var client = _factory.CreateApiClient();
        var (bank, _) = await SeedAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var savings = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Savings " + suffix, kind = "Asset", role = "SavingsPocket",
                  currencyCode = "EUR" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var created = await client.PostAsJsonAsync("/api/v1/transactions/transfer",
            new { amount = 200m, fromAccountId = bank, toAccountId = savings!.Id,
                  occurredOn = "2026-09-01" }, TestContext.Current.CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{savings.Id}/balance", TestContext.Current.CancellationToken);
        balance!.Balance.Should().Be(200m);
    }

    [Fact]
    public async Task A_split_transaction_can_be_posted_read_edited_and_voided()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        var created = await (await client.PostAsJsonAsync("/api/v1/transactions",
            new
            {
                occurredOn = "2026-09-01",
                description = "Shop",
                lines = new[]
                {
                    new { accountId = category, amount = 60m, memo = "Food" },
                    new { accountId = bank, amount = -60m, memo = (string?)null }
                }
            }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<TransactionDto>(TestContext.Current.CancellationToken);

        created!.Lines.Should().HaveCount(2);

        var fetched = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/v1/transactions/{created.Id}", TestContext.Current.CancellationToken);
        fetched!.Description.Should().Be("Shop");

        var replaced = await client.PutAsJsonAsync($"/api/v1/transactions/{created.Id}",
            new
            {
                occurredOn = "2026-09-02",
                description = "Shop (corrected)",
                lines = new[]
                {
                    new { accountId = category, amount = 65m, memo = (string?)null },
                    new { accountId = bank, amount = -65m, memo = (string?)null }
                }
            }, TestContext.Current.CancellationToken);
        replaced.StatusCode.Should().Be(HttpStatusCode.OK);

        var voided = await client.PostAsJsonAsync($"/api/v1/transactions/{created.Id}/void",
            new { reason = "Entered twice" }, TestContext.Current.CancellationToken);
        voided.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterVoid = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/v1/transactions/{created.Id}", TestContext.Current.CancellationToken);
        afterVoid!.IsVoided.Should().BeTrue();
    }

    [Fact]
    public async Task An_unbalanced_transaction_is_a_four_hundred_naming_the_shortfall()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/transactions",
            new
            {
                occurredOn = "2026-09-01",
                description = "Broken",
                lines = new[]
                {
                    new { accountId = category, amount = 60m },
                    new { accountId = bank, amount = -59m }
                }
            }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("do not balance");
    }

    [Fact]
    public async Task Income_posted_as_a_positive_number_reads_back_as_a_positive_number()
    {
        using var client = _factory.CreateApiClient();
        var (bank, _) = await SeedAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var salary = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Salary " + suffix, kind = "Income" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var created = await (await client.PostAsJsonAsync("/api/v1/transactions",
            new
            {
                occurredOn = "2026-09-01",
                description = "Salary",
                lines = new[]
                {
                    new { accountId = bank, amount = 3000m },
                    new { accountId = salary!.Id, amount = 3000m }
                }
            }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<TransactionDto>(TestContext.Current.CancellationToken);

        created!.Lines.Single(l => l.AccountId == salary!.Id).Amount.Should().Be(3000m);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{salary!.Id}/balance", TestContext.Current.CancellationToken);
        balance!.Balance.Should().Be(3000m, "income is shown positive; the sign lives in one mapper");
    }

    [Fact]
    public async Task The_transaction_list_pages_with_a_cursor()
    {
        using var client = _factory.CreateApiClient();
        var (bank, category) = await SeedAsync(client);

        for (var i = 1; i <= 5; i++)
        {
            await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
                new { amount = i, categoryId = category, accountId = bank,
                      occurredOn = $"2026-09-{i:D2}", description = $"Item {i}" },
                TestContext.Current.CancellationToken);
        }

        var first = await client.GetFromJsonAsync<TransactionPageDto>(
            $"/api/v1/transactions?accountId={bank}&limit=2", TestContext.Current.CancellationToken);

        first!.Items.Should().HaveCount(2);
        first.NextCursor.Should().NotBeNull();

        var second = await client.GetFromJsonAsync<TransactionPageDto>(
            $"/api/v1/transactions?accountId={bank}&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}",
            TestContext.Current.CancellationToken);

        second!.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id));
    }
}
```

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class IdempotencyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public IdempotencyTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_same_idempotency_key_posts_the_expense_only_once()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 1000m, openedOn = "2026-01-01" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Food " + suffix, kind = "Expense" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var key = Guid.NewGuid().ToString("N");

        async Task<HttpResponseMessage> PostAsync()
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/v1/transactions/quick-expense")
            {
                Content = JsonContent.Create(new
                {
                    amount = 42m, categoryId = category!.Id, accountId = bank!.Id,
                    occurredOn = "2026-09-01", description = "Retry me"
                })
            };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        var first = await PostAsync();
        var second = await PostAsync();

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.Contains("Idempotency-Replayed").Should().BeTrue();

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank!.Id}/balance", TestContext.Current.CancellationToken);

        balance!.Balance.Should().Be(958m, "the retry must not post a second expense");
    }

    [Fact]
    public async Task Two_different_keys_post_two_expenses()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 100m, openedOn = "2026-01-01" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Coffee " + suffix, kind = "Expense" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/v1/transactions/quick-expense")
            {
                Content = JsonContent.Create(new
                {
                    amount = 3m, categoryId = category!.Id, accountId = bank!.Id,
                    occurredOn = "2026-09-01", description = "Coffee"
                })
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank!.Id}/balance", TestContext.Current.CancellationToken);

        balance!.Balance.Should().Be(94m);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Api.Tests --filter "TransactionEndpointTests|IdempotencyTests"`
Expected: FAIL — the endpoints are not mapped.

- [ ] **Step 3: Implement `IdempotencyFilter`**

```csharp
using System.Text.Json;
using Money.Application.Abstractions;
using Money.Domain.Time;

namespace Money.Api.Infrastructure;

/// <summary>
/// Replays the response of a POST that carried an Idempotency-Key. Costs one table and this
/// filter; buys survival when a phone on a flaky connection retries (spec section 9).
/// </summary>
public sealed class IdempotencyFilter(IIdempotencyStore store, IUnitOfWork unitOfWork, IClock clock)
    : IEndpointFilter
{
    private const string HeaderName = "Idempotency-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(HeaderName, out var values)) return await next(context);

        var key = values.ToString();
        if (string.IsNullOrWhiteSpace(key)) return await next(context);

        var endpoint = http.Request.Path.Value ?? "";
        var replay = await store.TryGetResponseAsync(key, endpoint, http.RequestAborted);

        if (replay is not null)
        {
            http.Response.Headers["Idempotency-Replayed"] = "true";
            return Results.Content(replay, "application/json", statusCode: StatusCodes.Status200OK);
        }

        var result = await next(context);

        if (result is IValueHttpResult { Value: not null } valued
            && result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
        {
            await store.RecordAsync(
                key, endpoint, JsonSerializer.Serialize(valued.Value, JsonOptions),
                clock.UtcNow, http.RequestAborted);
            await unitOfWork.SaveChangesAsync(http.RequestAborted);
        }

        return result;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
```

This filter is the only caller that has a clock, which is why `RecordAsync` takes the timestamp as a parameter (Task 17) rather than reading it.

- [ ] **Step 4: Implement `TransactionEndpoints`**

```csharp
using Money.Api.Infrastructure;
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Application.Transactions;

namespace Money.Api.Endpoints;

public static class TransactionEndpoints
{
    public static RouteGroupBuilder MapTransactionEndpoints(this RouteGroupBuilder group)
    {
        var transactions = group.MapGroup("/transactions").WithTags("Transactions");

        transactions.MapGet("/", async (
            DateOnly? from, DateOnly? to, Guid? accountId, Guid? categoryId, string? q,
            bool? includeVoided, string? cursor, int? limit,
            ListTransactionsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(
                new TransactionQuery(from, to, accountId, categoryId, q,
                                     includeVoided ?? false, cursor, limit ?? 50),
                cancellationToken)))
            .WithName("ListTransactions");

        transactions.MapPost("/", async (
            CreateTransactionRequest request, CreateTransactionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/transactions/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .WithName("CreateTransaction");

        transactions.MapGet("/{id:guid}", async (
            Guid id, GetTransactionHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("GetTransaction");

        transactions.MapPut("/{id:guid}", async (
            Guid id, CreateTransactionRequest request, ReplaceTransactionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("ReplaceTransaction");

        transactions.MapPost("/{id:guid}/void", async (
            Guid id, VoidTransactionRequest request, VoidTransactionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, request.Reason, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : DomainErrorResults.ToProblem(result);
        }).WithName("VoidTransaction");

        transactions.MapPost("/quick-expense", async (
            QuickExpenseRequest request, QuickExpenseHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/transactions/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .WithName("QuickExpense");

        transactions.MapPost("/transfer", async (
            TransferRequest request, TransferHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/v1/transactions/{result.Value.Id}", result.Value)
                : DomainErrorResults.ToProblem(result);
        })
        .AddEndpointFilter<IdempotencyFilter>()
        .WithName("Transfer");

        return group;
    }
}
```

Register the filter: `services.AddScoped<IdempotencyFilter>();` in `DependencyInjection`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Api.Tests --filter "TransactionEndpointTests|IdempotencyTests"`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Money.Api tests/Money.Api.Tests
git commit -m "feat: add transaction endpoints with Idempotency-Key replay"
```

---

### Task 29: Admin endpoints — backup, export, integrity check

**Files:**
- Create: `src/Money.Api/Endpoints/AdminEndpoints.cs`
- Test: `tests/Money.Api.Tests/AdminEndpointTests.cs`

**Interfaces:**
- Produces: `MapAdminEndpoints()` covering `POST /admin/backup`, `GET /admin/export?format=json|csv`, `POST /admin/integrity-check`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class AdminEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AdminEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_integrity_check_reports_a_healthy_ledger()
    {
        using var client = _factory.CreateApiClient();

        var report = await (await client.PostAsync("/api/v1/admin/integrity-check", null,
                                                   TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<IntegrityReportDto>(TestContext.Current.CancellationToken);

        report!.IsHealthy.Should().BeTrue();
        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task The_json_export_downloads_as_a_file()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/admin/export?format=json",
                                             TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("money-export.json");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("schemaVersion");
    }

    [Fact]
    public async Task The_csv_export_downloads_with_the_flat_entry_header()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/admin/export?format=csv",
                                             TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().StartWith("TransactionId,OccurredOn,Description");
    }

    [Fact]
    public async Task An_unsupported_export_format_is_a_four_hundred()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/admin/export?format=xml",
                                             TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_export_never_contains_the_word_posting()
    {
        using var client = _factory.CreateApiClient();

        var json = await client.GetStringAsync("/api/v1/admin/export?format=json",
                                               TestContext.Current.CancellationToken);

        json.Should().NotContain("posting", "the export is a user-facing document");
        json.Should().Contain("entries");
    }
}
```

The backup endpoint is not exercised here: it writes to the real data directory. Task 25 already tests `SqliteBackupService` directly against a temp folder; testing the HTTP wrapper again would only test ASP.NET Core.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Api.Tests --filter AdminEndpointTests`
Expected: FAIL — the endpoints are not mapped.

- [ ] **Step 3: Implement `AdminEndpoints`**

```csharp
using System.Text;
using Money.Api.Infrastructure;
using Money.Application.Admin;

namespace Money.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin").WithTags("Admin");

        admin.MapPost("/backup", async (
            CreateBackupHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("CreateBackup");

        admin.MapGet("/export", async (
            string? format, ExportLedgerHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(format, cancellationToken);
            if (result.IsFailure) return DomainErrorResults.ToProblem(result);

            var (content, contentType, fileName) = result.Value;
            return Results.File(Encoding.UTF8.GetBytes(content), contentType, fileName);
        }).WithName("ExportLedger");

        admin.MapPost("/integrity-check", async (
            RunIntegrityCheckHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("RunIntegrityCheck");

        return group;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Api.Tests --filter AdminEndpointTests`
Expected: PASS, 5 tests.

If `The_export_never_contains_the_word_posting` fails, the JSON export is still naming the collection `postings`. Rename it to `entries` in `JsonExportService` — the export is a document the user opens.

- [ ] **Step 5: Commit**

```bash
git add src/Money.Api tests/Money.Api.Tests
git commit -m "feat: add admin endpoints for backup, export and integrity check"
```

---

### Task 30: The HTMX shell and the transactions screen

**Files:**
- Create: `src/Money.Api/Pages/_ViewImports.cshtml`, `_ViewStart.cshtml`, `Shared/_Layout.cshtml`
- Create: `src/Money.Api/Pages/Index.cshtml` (+ `.cs`), `Transactions.cshtml` (+ `.cs`)
- Create: `src/Money.Api/Pages/Shared/_TransactionRows.cshtml`, `_QuickAddRow.cshtml`
- Create: `src/Money.Api/wwwroot/app.css`, `src/Money.Api/wwwroot/lib/htmx.min.js`, `src/Money.Api/wwwroot/manifest.webmanifest`, `src/Money.Api/wwwroot/sw.js`
- Test: `tests/Money.Api.Tests/TransactionsPageTests.cs`

**Interfaces:**
- Consumes: `ListTransactionsHandler`, `QuickExpenseHandler`, `GetCategoryTreeHandler`, `ListAccountsHandler`, `GetSettingsHandler`.
- Produces: `TransactionsModel : PageModel` with `OnGetAsync(...)`, `OnPostQuickAddAsync(...)` returning the rows partial, `OnPostVoidAsync(Guid id, string reason)`.

**Vocabulary rule (spec section 14):** the words *posting*, *debit*, *credit* and *double-entry* must not appear in any `.cshtml`. Lines are **entries**. Expense accounts are **categories**. A test asserts this.

- [ ] **Step 1: Write the failing page tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class TransactionsPageTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TransactionsPageTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_transactions_page_renders()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/transactions", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.Should().Contain("Transactions");
        html.Should().Contain("htmx.min.js");
    }

    [Fact]
    public async Task No_page_uses_accounting_vocabulary()
    {
        using var client = _factory.CreateApiClient();

        // Task 32 widens this list to "/categories", "/accounts" and "/settings" once those
        // pages exist. Every page ever added goes in here.
        foreach (var path in new[] { "/transactions" })
        {
            var html = await client.GetStringAsync(path, TestContext.Current.CancellationToken);

            html.Should().NotContainEquivalentOf("posting", "the UI never says 'posting' ({0})", path);
            html.Should().NotContainEquivalentOf("double-entry", "({0})", path);
            html.Should().NotContainEquivalentOf(">debit<", "({0})", path);
            html.Should().NotContainEquivalentOf(">credit<", "({0})", path);
        }
    }

    [Fact]
    public async Task A_recorded_expense_shows_up_in_the_rendered_list()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 500m, openedOn = "2026-01-01" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Books " + suffix, kind = "Expense" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 19.99m, categoryId = category!.Id, accountId = bank!.Id,
                  occurredOn = "2026-09-01", description = "Novel " + suffix },
            TestContext.Current.CancellationToken);

        var html = await client.GetStringAsync("/transactions", TestContext.Current.CancellationToken);

        html.Should().Contain("Novel " + suffix);
        html.Should().Contain("19.99");
    }

    [Fact]
    public async Task The_page_is_responsive_and_declares_a_pwa_manifest()
    {
        using var client = _factory.CreateApiClient();

        var html = await client.GetStringAsync("/transactions", TestContext.Current.CancellationToken);

        html.Should().Contain("name=\"viewport\"");
        html.Should().Contain("manifest.webmanifest");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Api.Tests --filter TransactionsPageTests`
Expected: FAIL — the pages do not exist.

- [ ] **Step 3: Vendor HTMX**

Download HTMX 2.x and save it as `src/Money.Api/wwwroot/lib/htmx.min.js`. Commit the file — spec section 4: no Node, no build step, and a vendored asset works offline by definition.

```bash
curl -L https://unpkg.com/htmx.org@2.0.4/dist/htmx.min.js -o src/Money.Api/wwwroot/lib/htmx.min.js
```

Record the version in a one-line comment at the top of the file so a later upgrade is a deliberate act.

- [ ] **Step 4: Write the Razor plumbing**

`Pages/_ViewImports.cshtml`:

```cshtml
@using Money.Api
@using Money.Api.Pages
@using Money.Application.Contracts
@namespace Money.Api.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

`Pages/_ViewStart.cshtml`:

```cshtml
@{
    Layout = "Shared/_Layout";
}
```

`Pages/Shared/_Layout.cshtml`:

```cshtml
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>@ViewData["Title"] — Money</title>
    <link rel="stylesheet" href="~/app.css" />
    <link rel="manifest" href="~/manifest.webmanifest" />
    <script src="~/lib/htmx.min.js" defer></script>
</head>
<body>
    <header class="topbar">
        <a class="brand" href="/">Money</a>
        <nav>
            <a href="/transactions">Transactions</a>
            <a href="/categories">Categories</a>
            <a href="/accounts">Accounts</a>
            <a href="/settings">Settings</a>
        </nav>
    </header>

    <main>
        @RenderBody()
    </main>

    <script>
        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.register('/sw.js').catch(function () { });
        }
    </script>
</body>
</html>
```

`wwwroot/manifest.webmanifest`:

```json
{
  "name": "Money",
  "short_name": "Money",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#12141a",
  "theme_color": "#12141a",
  "icons": []
}
```

`wwwroot/sw.js` — deliberately minimal in v1; phase 9 makes it cache assets:

```javascript
// Registered now so that server mode installs to a home screen later without further work.
self.addEventListener('fetch', function () { });
```

`wwwroot/app.css` — a small, readable stylesheet. Keep it under 200 lines; this is a personal app, not a design system.

```css
:root {
    color-scheme: light dark;
    --bg: #12141a;
    --panel: #1a1d26;
    --text: #e7e9ee;
    --muted: #99a0b0;
    --accent: #6ea8fe;
    --danger: #ff6b6b;
    --ok: #51cf66;
}

* { box-sizing: border-box; }

body {
    margin: 0;
    font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif;
    background: var(--bg);
    color: var(--text);
}

.topbar {
    display: flex;
    gap: 1.5rem;
    align-items: baseline;
    padding: 0.75rem 1rem;
    background: var(--panel);
    position: sticky;
    top: 0;
}

.topbar nav { display: flex; gap: 1rem; flex-wrap: wrap; }
.topbar a { color: var(--text); text-decoration: none; }
.topbar a:hover { color: var(--accent); }
.brand { font-weight: 700; }

main { padding: 1rem; max-width: 60rem; margin: 0 auto; }

table { width: 100%; border-collapse: collapse; }
th, td { padding: 0.5rem; text-align: left; border-bottom: 1px solid #2a2e3a; }
td.amount, th.amount { text-align: right; font-variant-numeric: tabular-nums; }
tr.voided { opacity: 0.45; text-decoration: line-through; }

input, select, button {
    font: inherit;
    padding: 0.4rem 0.5rem;
    background: #232735;
    color: var(--text);
    border: 1px solid #333949;
    border-radius: 4px;
}

button { cursor: pointer; }
button.primary { background: var(--accent); color: #10131a; border-color: var(--accent); }
.error { color: var(--danger); }
.muted { color: var(--muted); }

@media (max-width: 40rem) {
    .quick-add { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem; }
    td.hide-narrow, th.hide-narrow { display: none; }
}
```

- [ ] **Step 5: Write `Index.cshtml`**

Phase 7 replaces this with the real dashboard. For now it is a signpost that also handles the first-run redirect.

`Pages/Index.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Settings;

namespace Money.Api.Pages;

public sealed class IndexModel(GetSettingsHandler settings) : PageModel
{
    public bool FirstRunCompleted { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        FirstRunCompleted = (await settings.HandleAsync(cancellationToken)).FirstRunCompleted;
        return FirstRunCompleted ? Page() : RedirectToPage("/FirstRun");
    }
}
```

`Pages/Index.cshtml`:

```cshtml
@page
@model IndexModel
@{ ViewData["Title"] = "Home"; }

<h1>Money</h1>
<p class="muted">The dashboard arrives in a later phase. For now, start here:</p>
<ul>
    <li><a href="/transactions">Record and review transactions</a></li>
    <li><a href="/categories">Manage categories</a></li>
    <li><a href="/accounts">Manage accounts</a></li>
</ul>
```

- [ ] **Step 6: Write the transactions page**

`Pages/Transactions.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;
using Money.Application.Transactions;
using Money.Domain.Time;

namespace Money.Api.Pages;

public sealed class TransactionsModel(
    ListTransactionsHandler list,
    QuickExpenseHandler quickExpense,
    VoidTransactionHandler voidTransaction,
    GetCategoryTreeHandler categories,
    ListAccountsHandler accounts,
    ISettingsRepository settingsRepository,
    IClock clock) : PageModel
{
    public TransactionPageDto Page { get; private set; } = new([], null);
    public IReadOnlyList<AccountDto> Accounts { get; private set; } = [];
    public IReadOnlyList<CategoryNodeDto> Categories { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public DateOnly Today { get; private set; }

    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? AccountId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CategoryId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool IncludeVoided { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostQuickAddAsync(
        [FromForm] decimal amount, [FromForm] Guid categoryId, [FromForm] Guid accountId,
        [FromForm] DateOnly? occurredOn, [FromForm] string? description,
        CancellationToken cancellationToken)
    {
        var result = await quickExpense.HandleAsync(
            new QuickExpenseRequest(amount, categoryId, accountId, occurredOn, description, null),
            cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_TransactionRows", this);
    }

    public async Task<IActionResult> OnPostVoidAsync(
        Guid id, string reason, CancellationToken cancellationToken)
    {
        var result = await voidTransaction.HandleAsync(
            id, string.IsNullOrWhiteSpace(reason) ? "Removed by the user" : reason, cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_TransactionRows", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        // The quick-add row's date defaults to today in the user's configured zone, never to
        // DateTime.Today - which would put a late-evening expense in yesterday.
        Today = await TodayResolver.TodayAsync(settingsRepository, clock, cancellationToken);

        Page = await list.HandleAsync(
            new TransactionQuery(From, To, AccountId, CategoryId, Q, IncludeVoided, null, 50),
            cancellationToken);

        Accounts = (await accounts.HandleAsync(null, null, false, cancellationToken))
            .Where(a => a.Kind == "Asset").ToArray();

        Categories = await categories.HandleAsync("Expense", false, cancellationToken);
    }
}
```

`TodayResolver` comes from Task 23; `ISettingsRepository` and `IClock` are already registered in DI by Task 26, so Razor Pages injects them into the model with no extra wiring.

`Pages/Transactions.cshtml`:

```cshtml
@page
@model TransactionsModel
@{ ViewData["Title"] = "Transactions"; }

<h1>Transactions</h1>

<form class="quick-add" method="post" asp-page-handler="QuickAdd"
      hx-post="/transactions?handler=QuickAdd" hx-target="#rows" hx-swap="outerHTML"
      hx-on::after-request="if(event.detail.successful) this.reset()">
    <input type="date" name="occurredOn" value="@Model.Today.ToString("yyyy-MM-dd")" required />
    <input type="number" name="amount" step="0.01" min="0.01" placeholder="Amount"
           autofocus required />
    <select name="categoryId" required>
        <option value="">Category…</option>
        @foreach (var category in Model.Categories)
        {
            <option value="@category.Id">@category.Name</option>
            @foreach (var child in category.Children)
            {
                <option value="@child.Id">@category.Name → @child.Name</option>
            }
        }
    </select>
    <select name="accountId" required>
        @foreach (var account in Model.Accounts)
        {
            <option value="@account.Id">@account.Name</option>
        }
    </select>
    <input type="text" name="description" placeholder="Description (optional)" />
    <button class="primary" type="submit">Add</button>
</form>

<form method="get" class="filters">
    <input type="search" name="q" value="@Model.Q" placeholder="Search…" />
    <input type="date" name="from" value="@(Model.From?.ToString("yyyy-MM-dd"))" />
    <input type="date" name="to" value="@(Model.To?.ToString("yyyy-MM-dd"))" />
    <label><input type="checkbox" name="includeVoided" value="true"
                  checked="@Model.IncludeVoided" /> Show removed</label>
    <button type="submit">Filter</button>
</form>

<partial name="Shared/_TransactionRows" model="Model" />
```

`Pages/Shared/_TransactionRows.cshtml`:

```cshtml
@model Money.Api.Pages.TransactionsModel

<div id="rows">
    @if (Model.ErrorMessage is not null)
    {
        <p class="error">@Model.ErrorMessage</p>
    }

    @if (Model.Page.Items.Count == 0)
    {
        <p class="muted">Nothing here yet. Add your first expense above.</p>
    }
    else
    {
        <table>
            <thead>
                <tr>
                    <th>Date</th>
                    <th>Description</th>
                    <th class="hide-narrow">Category</th>
                    <th class="hide-narrow">Account</th>
                    <th class="amount">Amount</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
            @foreach (var item in Model.Page.Items)
            {
                <tr class="@(item.IsVoided ? "voided" : null)">
                    <td>@item.OccurredOn.ToString("yyyy-MM-dd")</td>
                    <td>@item.Description</td>
                    <td class="hide-narrow">@item.CategoryName</td>
                    <td class="hide-narrow">@item.AccountName</td>
                    <td class="amount">@item.Amount.ToString("N2") @item.CurrencyCode</td>
                    <td>
                        @if (!item.IsVoided)
                        {
                            <button hx-post="@($"/transactions?handler=Void&id={item.Id}&reason=Removed+by+the+user")"
                                    hx-target="#rows" hx-swap="outerHTML"
                                    hx-confirm="Remove this transaction? It stays in the history, marked as removed.">
                                Remove
                            </button>
                        }
                    </td>
                </tr>
            }
            </tbody>
        </table>
    }
</div>
```

Razor Pages handlers require an antiforgery token for POSTs. Either add `@Html.AntiForgeryToken()` inside the forms and configure HTMX to send it, or register `services.AddAntiforgery()` and add `hx-headers='{"RequestVerificationToken": "..."}'`. Simplest robust approach for this app: put `<input name="__RequestVerificationToken" type="hidden" value="@Antiforgery.GetAndStoreTokens(HttpContext).RequestToken" />` inside each form and inject `IAntiforgery Antiforgery` into the layout. Do this now, not later — a 400 from a missing token is a confusing first bug to hit.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Api.Tests --filter TransactionsPageTests`
Expected: PASS, 4 tests.

`No_page_uses_accounting_vocabulary` covers only `/transactions` at this point, because that is the only page written. Task 32 widens it to every page. `/` is deliberately excluded here too: on a fresh database it redirects to the first-run wizard, so fetching it would assert against a redirect body.

- [ ] **Step 8: Commit**

```bash
git add src/Money.Api tests/Money.Api.Tests
git commit -m "feat: add HTMX shell and the transactions screen with quick-add"
```

---

### Task 31: Categories and accounts screens

**Files:**
- Create: `src/Money.Api/Pages/Categories.cshtml` (+ `.cs`), `Accounts.cshtml` (+ `.cs`)
- Create: `src/Money.Api/Pages/Shared/_CategoryTree.cshtml`, `_AccountRows.cshtml`
- Test: `tests/Money.Api.Tests/CategoriesPageTests.cs`

**Interfaces:**
- Produces: `CategoriesModel : PageModel` with `OnGetAsync`, `OnPostCreateAsync(string name, Guid? parentCategoryId)`, `OnPostRenameAsync(Guid id, string name)`, `OnPostArchiveAsync(Guid id)`; `AccountsModel : PageModel` with `OnGetAsync`, `OnPostCreateAsync(...)`, `OnPostArchiveAsync(Guid id)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class CategoriesPageTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CategoriesPageTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_categories_page_renders_the_tree()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var parent = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Travel " + suffix, kind = "Expense" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Flights", kind = "Expense", parentCategoryId = parent!.Id },
            TestContext.Current.CancellationToken);

        var html = await client.GetStringAsync("/categories", TestContext.Current.CancellationToken);

        html.Should().Contain("Travel " + suffix);
        html.Should().Contain("Flights");
    }

    [Fact]
    public async Task The_accounts_page_shows_balances()
    {
        using var client = _factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Vault " + suffix, kind = "Asset", role = "Cash", currencyCode = "EUR",
                  openingBalance = 42m, openedOn = "2026-01-01" },
            TestContext.Current.CancellationToken);

        var html = await client.GetStringAsync("/accounts", TestContext.Current.CancellationToken);

        html.Should().Contain("Vault " + suffix);
        html.Should().Contain("42.00");
    }

    [Fact]
    public async Task The_accounts_page_never_shows_the_equity_plumbing()
    {
        using var client = _factory.CreateApiClient();

        var html = await client.GetStringAsync("/accounts", TestContext.Current.CancellationToken);

        html.Should().NotContain("Equity", "the opening-balance account is bookkeeping, not a user account");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Api.Tests --filter CategoriesPageTests`
Expected: FAIL — the pages do not exist.

- [ ] **Step 3: Implement `CategoriesModel` and its view**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;

namespace Money.Api.Pages;

public sealed class CategoriesModel(
    GetCategoryTreeHandler tree,
    CreateCategoryHandler create,
    PatchAccountHandler patch,
    ArchiveAccountHandler archive) : PageModel
{
    public IReadOnlyList<CategoryNodeDto> Nodes { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "Expense";
    [BindProperty(SupportsGet = true)] public bool IncludeArchived { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] string name, [FromForm] Guid? parentCategoryId, CancellationToken cancellationToken)
    {
        var result = await create.HandleAsync(
            new CreateCategoryRequest(name, Kind, parentCategoryId), cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_CategoryTree", this);
    }

    public async Task<IActionResult> OnPostRenameAsync(
        Guid id, [FromForm] string name, CancellationToken cancellationToken)
    {
        var result = await patch.HandleAsync(
            id, new PatchAccountRequest(name, null, null, null, null, null, null), cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_CategoryTree", this);
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await archive.HandleAsync(id, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_CategoryTree", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken) =>
        Nodes = await tree.HandleAsync(Kind, IncludeArchived, cancellationToken);
}
```

`Pages/Categories.cshtml`:

```cshtml
@page
@model CategoriesModel
@{ ViewData["Title"] = "Categories"; }

<h1>Categories</h1>

<form method="get">
    <select name="kind" onchange="this.form.submit()">
        <option value="Expense" selected="@(Model.Kind == "Expense")">Spending</option>
        <option value="Income" selected="@(Model.Kind == "Income")">Income</option>
    </select>
    <label><input type="checkbox" name="includeArchived" value="true"
                  checked="@Model.IncludeArchived" onchange="this.form.submit()" /> Show archived</label>
</form>

<form hx-post="@($"/categories?handler=Create&kind={Model.Kind}")"
      hx-target="#tree" hx-swap="outerHTML"
      hx-on::after-request="if(event.detail.successful) this.reset()">
    <input type="text" name="name" placeholder="New category" required />
    <select name="parentCategoryId">
        <option value="">Top level</option>
        @foreach (var node in Model.Nodes)
        {
            <option value="@node.Id">@node.Name</option>
        }
    </select>
    <button class="primary" type="submit">Add</button>
</form>

<partial name="Shared/_CategoryTree" model="Model" />
```

`Pages/Shared/_CategoryTree.cshtml`:

```cshtml
@model Money.Api.Pages.CategoriesModel

<div id="tree">
    @if (Model.ErrorMessage is not null)
    {
        <p class="error">@Model.ErrorMessage</p>
    }

    @if (Model.Nodes.Count == 0)
    {
        <p class="muted">No categories yet.</p>
    }
    else
    {
        <ul>
            @foreach (var node in Model.Nodes)
            {
                @await Html.PartialAsync("Shared/_CategoryNode", node)
            }
        </ul>
    }
</div>
```

`Pages/Shared/_CategoryNode.cshtml`:

```cshtml
@model Money.Application.Contracts.CategoryNodeDto

<li class="@(Model.IsArchived ? "muted" : null)">
    <span>@Model.Name</span>
    <button hx-post="@($"/categories?handler=Archive&id={Model.Id}")"
            hx-target="#tree" hx-swap="outerHTML"
            hx-confirm="Archive this category? Past transactions keep it.">Archive</button>

    @if (Model.Children.Count > 0)
    {
        <ul>
            @foreach (var child in Model.Children)
            {
                @await Html.PartialAsync("Shared/_CategoryNode", child)
            }
        </ul>
    }
</li>
```

- [ ] **Step 4: Implement `AccountsModel` and its view**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Accounts;
using Money.Application.Contracts;

namespace Money.Api.Pages;

public sealed class AccountsModel(
    ListAccountsHandler list,
    GetAccountBalanceHandler balances,
    CreateAccountHandler create,
    ArchiveAccountHandler archive) : PageModel
{
    public sealed record Row(AccountDto Account, decimal Balance);

    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)] public bool IncludeArchived { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] string name, [FromForm] string role, [FromForm] decimal? openingBalance,
        [FromForm] DateOnly? openedOn, CancellationToken cancellationToken)
    {
        var result = await create.HandleAsync(
            new CreateAccountRequest(name, "Asset", role, null, "EUR", openingBalance, openedOn),
            cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_AccountRows", this);
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await archive.HandleAsync(id, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_AccountRows", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        // Only the accounts a person thinks of as accounts. The Equity/OpeningBalance account is
        // bookkeeping and never appears here.
        var visible = (await list.HandleAsync("Asset", null, IncludeArchived, cancellationToken))
            .Where(a => a.Role is "Bank" or "Cash" or "SavingsPocket" or "Investment")
            .ToArray();

        var rows = new List<Row>(visible.Length);
        foreach (var account in visible)
        {
            var balance = await balances.HandleAsync(account.Id, null, cancellationToken);
            rows.Add(new Row(account, balance.IsSuccess ? balance.Value.Balance : 0m));
        }

        Rows = rows;
    }
}
```

`Pages/Accounts.cshtml`:

```cshtml
@page
@model AccountsModel
@{ ViewData["Title"] = "Accounts"; }

<h1>Accounts</h1>

<form hx-post="/accounts?handler=Create" hx-target="#accountRows" hx-swap="outerHTML"
      hx-on::after-request="if(event.detail.successful) this.reset()">
    <input type="text" name="name" placeholder="Account name" required />
    <select name="role">
        <option value="Bank">Bank</option>
        <option value="Cash">Cash</option>
        <option value="SavingsPocket">Savings</option>
        <option value="Investment">Investment</option>
    </select>
    <input type="number" name="openingBalance" step="0.01" placeholder="Starting balance" />
    <input type="date" name="openedOn" />
    <button class="primary" type="submit">Add</button>
</form>

<partial name="Shared/_AccountRows" model="Model" />
```

`Pages/Shared/_AccountRows.cshtml`:

```cshtml
@model Money.Api.Pages.AccountsModel

<div id="accountRows">
    @if (Model.ErrorMessage is not null)
    {
        <p class="error">@Model.ErrorMessage</p>
    }

    <table>
        <thead>
            <tr><th>Account</th><th>Type</th><th class="amount">Balance</th><th></th></tr>
        </thead>
        <tbody>
        @foreach (var row in Model.Rows)
        {
            <tr class="@(row.Account.IsArchived ? "muted" : null)">
                <td>@row.Account.Name</td>
                <td>@(row.Account.Role == "SavingsPocket" ? "Savings" : row.Account.Role)</td>
                <td class="amount">@row.Balance.ToString("N2") @row.Account.CurrencyCode</td>
                <td>
                    @if (!row.Account.IsArchived)
                    {
                        <button hx-post="@($"/accounts?handler=Archive&id={row.Account.Id}")"
                                hx-target="#accountRows" hx-swap="outerHTML"
                                hx-confirm="Archive this account? Its history is kept.">Archive</button>
                    }
                </td>
            </tr>
        }
        </tbody>
    </table>
</div>
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Api.Tests --filter CategoriesPageTests`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Money.Api tests/Money.Api.Tests
git commit -m "feat: add categories tree editor and accounts screens"
```

---

### Task 32: First-run wizard and the settings screen

**Files:**
- Create: `src/Money.Api/Pages/FirstRun.cshtml` (+ `.cs`), `Settings.cshtml` (+ `.cs`)
- Modify: `tests/Money.Api.Tests/TransactionsPageTests.cs` (restore the full path list in the vocabulary test)
- Test: `tests/Money.Api.Tests/FirstRunPageTests.cs`

**Interfaces:**
- Produces: `FirstRunModel : PageModel` with `OnGetAsync`, `OnPostAsync(FirstRunRequest)`; `SettingsModel : PageModel` with `OnGetAsync`, `OnPostSaveAsync(UpdateSettingsRequest)`, `OnPostBackupAsync()`, `OnPostIntegrityCheckAsync()`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;

namespace Money.Api.Tests;

public sealed class FirstRunPageTests
{
    [Fact]
    public async Task A_fresh_database_redirects_the_home_page_to_the_wizard()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("FirstRun");
    }

    [Fact]
    public async Task The_wizard_offers_a_currency_a_period_anchor_and_a_first_account()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var html = await client.GetStringAsync("/FirstRun", TestContext.Current.CancellationToken);

        html.Should().Contain("Currency");
        html.Should().Contain("EUR");
        html.Should().Contain("starter categories");
        html.Should().Contain("kept in your user folder",
            "the wizard explains where the data lives (spec section 14)");
    }

    [Fact]
    public async Task Completing_the_wizard_seeds_the_app_and_stops_redirecting()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["BaseCurrencyCode"] = "EUR",
            ["PeriodAnchor"] = "CalendarMonth",
            ["PeriodAnchorDay"] = "1",
            ["TimeZoneId"] = "Europe/Budapest",
            ["FirstDayOfWeek"] = "Monday",
            ["FirstAccountName"] = "Current account",
            ["FirstAccountRole"] = "Bank",
            ["OpeningBalance"] = "1500.00",
            ["OpenedOn"] = "2026-01-01",
            ["SeedStarterCategories"] = "true"
        });

        var response = await client.PostAsync("/FirstRun", form, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        home.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await client.GetStringAsync("/categories",
                                                     TestContext.Current.CancellationToken);
        categories.Should().Contain("Groceries").And.Contain("Alcohol").And.Contain("Gaming");
    }

    [Fact]
    public async Task The_settings_page_renders_and_can_run_an_integrity_check()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var html = await client.GetStringAsync("/settings", TestContext.Current.CancellationToken);
        html.Should().Contain("Backup").And.Contain("Export").And.Contain("Integrity");

        var checkResponse = await client.PostAsync("/settings?handler=IntegrityCheck", null,
                                                   TestContext.Current.CancellationToken);
        checkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

Each test constructs its own `ApiFactory` so that the first-run state is genuinely fresh.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Money.Api.Tests --filter FirstRunPageTests`
Expected: FAIL — the pages do not exist.

- [ ] **Step 3: Implement the first-run wizard**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Contracts;
using Money.Application.FirstRun;
using Money.Application.Settings;

namespace Money.Api.Pages;

public sealed class FirstRunModel(
    CompleteFirstRunSetupHandler complete, GetSettingsHandler settings) : PageModel
{
    [BindProperty] public string BaseCurrencyCode { get; set; } = "EUR";
    [BindProperty] public string PeriodAnchor { get; set; } = "CalendarMonth";
    [BindProperty] public int PeriodAnchorDay { get; set; } = 1;
    [BindProperty] public string TimeZoneId { get; set; } = "Europe/Budapest";
    [BindProperty] public string FirstDayOfWeek { get; set; } = "Monday";
    [BindProperty] public string FirstAccountName { get; set; } = "Current account";
    [BindProperty] public string FirstAccountRole { get; set; } = "Bank";
    [BindProperty] public decimal OpeningBalance { get; set; }
    [BindProperty] public DateOnly OpenedOn { get; set; }
    [BindProperty] public bool SeedStarterCategories { get; set; } = true;

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        (await settings.HandleAsync(cancellationToken)).FirstRunCompleted
            ? RedirectToPage("/Index")
            : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await complete.HandleAsync(new FirstRunRequest(
            BaseCurrencyCode, PeriodAnchor, PeriodAnchorDay, TimeZoneId, FirstDayOfWeek,
            FirstAccountName, FirstAccountRole, OpeningBalance, OpenedOn, SeedStarterCategories),
            cancellationToken);

        if (result.IsFailure)
        {
            ErrorMessage = result.Error!.Message;
            return Page();
        }

        return RedirectToPage("/Transactions");
    }
}
```

`Pages/FirstRun.cshtml`:

```cshtml
@page
@model FirstRunModel
@{ ViewData["Title"] = "Welcome"; }

<h1>Welcome</h1>
<p class="muted">
    Three quick choices and you can start recording. Everything is stored on this computer only:
    your data is kept in your user folder, never beside the program and never online.
</p>

@if (Model.ErrorMessage is not null)
{
    <p class="error">@Model.ErrorMessage</p>
}

<form method="post">
    <fieldset>
        <legend>Currency</legend>
        <select asp-for="BaseCurrencyCode">
            <option value="EUR">EUR</option>
            <option value="HUF">HUF</option>
            <option value="USD">USD</option>
            <option value="GBP">GBP</option>
        </select>
    </fieldset>

    <fieldset>
        <legend>When does your month start?</legend>
        <label><input type="radio" asp-for="PeriodAnchor" value="CalendarMonth" checked />
            On the 1st</label>
        <label><input type="radio" asp-for="PeriodAnchor" value="DayOfMonth" />
            On a payday: day
            <input type="number" asp-for="PeriodAnchorDay" min="1" max="28" /></label>
        <p class="muted">Days above 28 are not offered: not every month has them.</p>
        <label>Time zone <input type="text" asp-for="TimeZoneId" /></label>
        <label>Week starts on
            <select asp-for="FirstDayOfWeek">
                <option value="Monday">Monday</option>
                <option value="Sunday">Sunday</option>
            </select>
        </label>
    </fieldset>

    <fieldset>
        <legend>Your first account</legend>
        <input type="text" asp-for="FirstAccountName" required />
        <select asp-for="FirstAccountRole">
            <option value="Bank">Bank</option>
            <option value="Cash">Cash</option>
        </select>
        <label>Starting balance <input type="number" asp-for="OpeningBalance" step="0.01" /></label>
        <label>As of <input type="date" asp-for="OpenedOn" required /></label>
    </fieldset>

    <label><input type="checkbox" asp-for="SeedStarterCategories" />
        Create a starter set of categories (Housing, Groceries, Eating out, Alcohol, Gaming,
        Transport, Health, Subscriptions, Other). You can rename or remove any of them.</label>

    <button class="primary" type="submit">Start</button>
</form>
```

- [ ] **Step 4: Implement the settings screen**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Admin;
using Money.Application.Contracts;
using Money.Application.Settings;

namespace Money.Api.Pages;

public sealed class SettingsModel(
    GetSettingsHandler get,
    UpdateSettingsHandler update,
    CreateBackupHandler backup,
    RunIntegrityCheckHandler integrity) : PageModel
{
    public SettingsDto Current { get; private set; } = null!;
    public string? Message { get; private set; }
    public string? ErrorMessage { get; private set; }
    public IntegrityReportDto? Report { get; private set; }

    [BindProperty] public UpdateSettingsRequest Form { get; set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var result = await update.HandleAsync(Form, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else Message = "Saved.";

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostBackupAsync(CancellationToken cancellationToken)
    {
        var result = await backup.HandleAsync(cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else Message = $"Backup written: {result.Value.FileName}";

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostIntegrityCheckAsync(CancellationToken cancellationToken)
    {
        Report = await integrity.HandleAsync(cancellationToken);
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Current = await get.HandleAsync(cancellationToken);
        Form ??= new UpdateSettingsRequest(
            Current.BaseCurrencyCode, Current.PeriodAnchor, Current.PeriodAnchorDay,
            Current.TimeZoneId, Current.FirstDayOfWeek, Current.BackupRetentionCount);
    }
}
```

`Pages/Settings.cshtml`:

```cshtml
@page
@model SettingsModel
@{ ViewData["Title"] = "Settings"; }

<h1>Settings</h1>

@if (Model.Message is not null) { <p class="ok">@Model.Message</p> }
@if (Model.ErrorMessage is not null) { <p class="error">@Model.ErrorMessage</p> }

<form method="post" asp-page-handler="Save">
    <label>Currency <input asp-for="Form.BaseCurrencyCode" maxlength="3" /></label>
    <label>Month starts
        <select asp-for="Form.PeriodAnchor">
            <option value="CalendarMonth">On the 1st</option>
            <option value="DayOfMonth">On a payday</option>
        </select>
    </label>
    <label>Payday <input type="number" asp-for="Form.PeriodAnchorDay" min="1" max="28" /></label>
    <label>Time zone <input asp-for="Form.TimeZoneId" /></label>
    <label>Week starts on <input asp-for="Form.FirstDayOfWeek" /></label>
    <label>Backups to keep <input type="number" asp-for="Form.BackupRetentionCount" min="1" max="100" /></label>
    <button class="primary" type="submit">Save</button>
</form>

<h2>Backup</h2>
<p class="muted">Backups are written next to your data, in your user folder.</p>
<form method="post" asp-page-handler="Backup"><button type="submit">Back up now</button></form>

<h2>Export</h2>
<p class="muted">
    Amounts are exported in whole cents so a spreadsheet cannot round them.
</p>
<a href="/api/v1/admin/export?format=json">Download JSON</a>
<a href="/api/v1/admin/export?format=csv">Download CSV</a>

<h2>Integrity</h2>
<form method="post" asp-page-handler="IntegrityCheck">
    <button type="submit">Check my data</button>
</form>

@if (Model.Report is not null)
{
    @if (Model.Report.IsHealthy)
    {
        <p class="ok">Everything checks out.</p>
    }
    else
    {
        <ul class="error">
            @foreach (var finding in Model.Report.Findings)
            {
                <li>@finding</li>
            }
        </ul>
    }
}
```

- [ ] **Step 5: Restore the full vocabulary test**

Put `/categories`, `/accounts` and `/settings` back into `No_page_uses_accounting_vocabulary`'s path list, and run it against a factory that has completed first run (otherwise `/` redirects).

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Money.Api.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Money.Api tests/Money.Api.Tests
git commit -m "feat: add first-run wizard and settings screen with backup, export and integrity check"
```

---

### Task 33: Phase 2 acceptance — the round-trip, and a vocabulary review

**Files:**
- Test: `tests/Money.Api.Tests/RoundTripAcceptanceTests.cs`
- Create: `docs/running-locally.md`

**Interfaces:**
- Consumes: everything.
- Produces: the acceptance evidence for phase 2 and a short document telling a human how to run the thing.

Spec section 13, phase 2 acceptance: *"a usable manual expense tracker. Round-trip: add expense, see it in the list, see the balance change."* This task turns that sentence into an executable test.

- [ ] **Step 1: Write the failing acceptance test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

/// <summary>
/// Spec section 13, phase 2: "A usable manual expense tracker. Round-trip: add expense, see it in
/// the list, see the balance change." This test is that sentence.
/// </summary>
public sealed class RoundTripAcceptanceTests
{
    [Fact]
    public async Task A_new_user_can_set_up_record_an_expense_and_see_it_everywhere()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        // 1. First run: currency, payday anchor, first account, starter categories.
        var wizard = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["BaseCurrencyCode"] = "EUR",
            ["PeriodAnchor"] = "DayOfMonth",
            ["PeriodAnchorDay"] = "25",
            ["TimeZoneId"] = "Europe/Budapest",
            ["FirstDayOfWeek"] = "Monday",
            ["FirstAccountName"] = "Current account",
            ["FirstAccountRole"] = "Bank",
            ["OpeningBalance"] = "1500.00",
            ["OpenedOn"] = "2026-01-01",
            ["SeedStarterCategories"] = "true"
        });

        (await client.PostAsync("/FirstRun", wizard, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        var accounts = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/v1/accounts?kind=Asset", TestContext.Current.CancellationToken);
        var bank = accounts!.Single(a => a.Name == "Current account");

        var categories = await client.GetFromJsonAsync<List<CategoryNodeDto>>(
            "/api/v1/categories?kind=Expense", TestContext.Current.CancellationToken);
        var groceries = categories!.Single(c => c.Name == "Groceries");

        // 2. Record an expense.
        var expense = await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 42.35m, categoryId = groceries.Id, accountId = bank.Id,
                  occurredOn = "2026-09-01", description = "Weekly shop" },
            TestContext.Current.CancellationToken);
        expense.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. See it in the list.
        var page = await client.GetFromJsonAsync<TransactionPageDto>(
            "/api/v1/transactions", TestContext.Current.CancellationToken);
        page!.Items.Should().Contain(i => i.Description == "Weekly shop" && i.Amount == 42.35m);

        var html = await client.GetStringAsync("/transactions", TestContext.Current.CancellationToken);
        html.Should().Contain("Weekly shop").And.Contain("42.35");

        // 4. See the balance change.
        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank.Id}/balance", TestContext.Current.CancellationToken);
        balance!.Balance.Should().Be(1457.65m);

        // 5. Move money to savings, and confirm it is not spending.
        var savings = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Rainy day", kind = "Asset", role = "SavingsPocket", currencyCode = "EUR" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync("/api/v1/transactions/transfer",
            new { amount = 300m, fromAccountId = bank.Id, toAccountId = savings!.Id,
                  occurredOn = "2026-09-02" }, TestContext.Current.CancellationToken);

        var groceriesBalance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{groceries.Id}/balance", TestContext.Current.CancellationToken);
        groceriesBalance!.Balance.Should().Be(42.35m,
            "moving money to savings is not spending (I12)");

        // 6. The books still balance, and the data passes its own integrity check.
        var report = await (await client.PostAsync("/api/v1/admin/integrity-check", null,
                                                   TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<IntegrityReportDto>(TestContext.Current.CancellationToken);
        report!.IsHealthy.Should().BeTrue();

        // 7. Everything can be exported.
        var export = await client.GetAsync("/api/v1/admin/export?format=json",
                                           TestContext.Current.CancellationToken);
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        (await export.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Weekly shop");
    }

    [Fact]
    public async Task A_mistaken_entry_can_be_removed_and_the_balance_returns()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Wallet", kind = "Asset", role = "Cash", currencyCode = "EUR",
                  openingBalance = 100m, openedOn = "2026-01-01" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Snacks", kind = "Expense" }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<AccountDto>(TestContext.Current.CancellationToken);

        var created = await (await client.PostAsJsonAsync("/api/v1/transactions/quick-expense",
            new { amount = 9.99m, categoryId = category!.Id, accountId = bank!.Id,
                  occurredOn = "2026-09-01", description = "Oops" },
            TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<TransactionDto>(TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync($"/api/v1/transactions/{created!.Id}/void",
            new { reason = "Entered twice" }, TestContext.Current.CancellationToken);

        var balance = await client.GetFromJsonAsync<AccountBalanceDto>(
            $"/api/v1/accounts/{bank.Id}/balance", TestContext.Current.CancellationToken);
        balance!.Balance.Should().Be(100m);

        var withVoided = await client.GetFromJsonAsync<TransactionPageDto>(
            "/api/v1/transactions?includeVoided=true", TestContext.Current.CancellationToken);
        withVoided!.Items.Should().Contain(i => i.Id == created.Id,
            "history is kept; nothing is ever deleted (spec D11)");
    }
}
```

- [ ] **Step 2: Run the acceptance test**

Run: `dotnet test tests/Money.Api.Tests --filter RoundTripAcceptanceTests`
Expected: PASS. If it does not, the phase is not done — fix the product, not the test.

- [ ] **Step 3: Run everything**

Run each and confirm:

```bash
dotnet build -warnaserror -c Release
```

```bash
dotnet test -c Release
```

```bash
dotnet stryker
```

(the last from `src/Money.Domain`, expecting a score at or above 80).

Record the actual numbers — total tests, mutation score — in the commit message. "All green" without numbers is not evidence.

- [ ] **Step 4: Do the vocabulary review by hand**

Spec section 14 requires a UI review at the end of phase 2. Open every page in a browser (`dotnet run --project src/Money.Api`, then `http://localhost:5xxx`) and read it as the user would:

- Does any label say *posting*, *entry pair*, *debit*, *credit*, *ledger*, *double-entry*? Fix it.
- Does the accounts screen show the `Opening balance` equity account? It must not.
- Does income read as a positive number everywhere?
- Does the quick-add row let you record an expense in under five seconds — date pre-filled, amount focused, category type-ahead, `Enter` saves? Success criterion 1 depends on it. If `Enter` does not save, add `type="submit"` to the add button and confirm the form submits on `Enter` in the amount field.

Fix what you find, with a test for anything that was wrong.

- [ ] **Step 5: Write `docs/running-locally.md`**

```markdown
# Running Money locally

## Prerequisites

- .NET 9 SDK

## Run the app

    dotnet run --project src/Money.Api

Open the URL printed in the console. On first run you are taken to a short setup wizard.

## Where your data lives

`%APPDATA%\MoneyApp\money.db` — deliberately **not** in the project folder, which is on the
Desktop and may be synced by OneDrive. A synced SQLite file in WAL mode can be corrupted.

Override with the `MONEYAPP_DATA_DIR` environment variable.

Backups are written to `%APPDATA%\MoneyApp\backups\`, newest ten kept by default.

## Tests

    dotnet test

    dotnet stryker      # from src/Money.Domain — mutation score must stay at or above 80

## Database changes

    dotnet ef migrations add <Name> --project src/Money.Infrastructure --output-dir Persistence/Migrations

Migrations are applied at startup, after an automatic backup.
```

- [ ] **Step 6: Commit**

```bash
git add tests/Money.Api.Tests docs/running-locally.md
git commit -m "test: add the phase 2 round-trip acceptance test and local run docs"
```

**Phase 2 acceptance:** the round-trip test passes end to end — first run, record an expense, see it in the API and in the rendered page, see the balance change, move money to savings without it counting as spending, pass the integrity check, export everything. `dotnet build -warnaserror`, `dotnet test` and `dotnet stryker` all pass. The vocabulary review is done and its fixes are committed.

---

## What phases 3–8 inherit from this plan

Written down so the next plan does not re-derive it, and so nothing here gets "cleaned up" by someone who does not know it is load-bearing:

| Seam built here | Used by |
|---|---|
| `PeriodResolver` — the only period arithmetic | Budgets (4), pockets (5), dashboard (7) |
| `TransactionSourceKind.Recurring` / `.Accrual` + the partial unique index `UX_Transactions_Source_Idempotency` | Recurring materialiser (3), accrual engine (6) — this index is what makes "rent posts once" a database guarantee rather than a hope |
| `Money.RoundToMinor` — the single rounding function | Accrual engine's carried remainder (6) |
| `AccountRole.SavingsPocket` / `.Investment` | Pockets (5), investments (6) |
| `BalanceCalculator` — the reference implementation | Every report; the golden dashboard test (7) checks against it |
| `LedgerGen` + `LedgerSeeder` in `Money.TestSupport` | Every later property test and the golden dashboard dataset (7) |
| `DisplayAmountMapper` — the only sign flip | Every DTO, forever |
| `IIntegrityChecker` | Admin screen; extended with pocket over-allocation (I6) in phase 5 |
| `HostingMode` | Server mode (9) |
| `SettingsEntity.Window*` columns | Window-state persistence (8) |
| PWA manifest and service worker | Phone install (9) |
| `IBackupService.ListBackupsAsync` | The **restore** path (8) — deferred here because swapping the database file requires a host to restart; see the deferrals note at the top of this plan |
| Cursor paging in `ILedgerQueries.ListAsync`; multi-line `POST /transactions` | Infinite scroll and the split editor in the phase 7 UI pass |

**Do not** in a later phase: store a balance, add a second rounding helper, compute a month boundary outside `PeriodResolver`, or flip a sign outside `DisplayAmountMapper`. Each of those is a bug the architecture tests cannot catch but a reviewer can.
