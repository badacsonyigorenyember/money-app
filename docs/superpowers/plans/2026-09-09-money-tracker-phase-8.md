# Money Tracker — Implementation Plan, Phase 8 (Packaging)

**Spec:** [2026-09-01-money-tracker-design.md](../specs/2026-09-01-money-tracker-design.md),
sections 8 (restore), 11 (hosting modes, desktop packaging) and 13 (phase 8 row).

**Phase 8 acceptance (spec section 13):** *"Double-clicking `MoneyApp.exe` on a clean
Windows 11 machine opens a working app."*

## What already exists

`src/Money.Desktop` is not empty. It boots `MoneyWebApp` on `http://127.0.0.1:0`,
opens a 1280x860 WinForms window with a docked `WebView2`, and redirects the WebView2
cache into the data directory. `dotnet publish src/Money.Desktop -c Release -o dist`
produces a self-contained `Money.exe` alongside a correct `wwwroot`.

So **feature parity between the desktop app and the web app is structural**: the desktop
app *is* the web app in a window. Nothing in this phase adds or duplicates a feature.
Everything in this phase is the difference between "a window with the app in it" and
"an application you can hand to someone".

## What is missing

| # | Gap | Evidence |
|---|---|---|
| G1 | No WebView2 Runtime presence check | Spec section 11 requires one; `Program.cs` constructs `WebView2` bare |
| G2 | No error handling at all in the host | `WinExe` plus an exception on startup means a silent exit: no window, no message |
| G3 | No single-instance guard | Spec section 11 requires a named mutex. Two instances means two Kestrels writing one SQLite file |
| G4 | Window size and position are hard-coded | `SettingsEntity.WindowWidth/Height/X/Y` exist, are nullable, and are read by nothing |
| G5 | No database restore | Deferred from phase 2 *because* there was no host process to restart. There is one now |
| G6 | No icon, no product metadata, publish output carries pdb, xml and deps.json clutter | `dotnet publish` output listing |
| G7 | CI never builds or runs the packaged executable | `.github/workflows/ci.yml` is two `ubuntu-latest` jobs |

## Task list

Tasks 1 to 3 are independent of each other and touch disjoint files. Task 4 depends on
1 and 2 landing first, because it is the only task that edits `Money.Desktop/Program.cs`.

### Task 1 — Window state, below the host

Add `WindowWidth`, `WindowHeight`, `WindowX` and `WindowY` to `AppSettings` and to
`SettingsRepository`'s mapping (all four nullable, all four already columns, so no
migration). Add a narrow application use case pair so the host never touches EF
directly: `GetWindowStateHandler` and `SaveWindowStateHandler`, the latter writing only
the four window fields and leaving every business field untouched.

`UpdateSettingsHandler` must keep window state the way it already keeps
`FirstRunCompleted` — a save from the Settings screen must not blank the window
position.

Tests (`Money.Application.Tests`, real SQLite): window state round-trips; saving window
state does not disturb business settings; saving business settings does not disturb
window state; a fresh database reports null window state.

### Task 2 — Restore

The spec's sentence is *"pick a backup, the app validates it, swaps the file and
restarts"*. That splits across three moments, because a live app cannot swap the file it
has open — Microsoft.Data.Sqlite pools native handles past `Dispose`, and WAL means
three files, not one.

1. **List.** `GET /api/v1/admin/backups`, over the existing and currently unused
   `IBackupService.ListBackupsAsync`.
2. **Stage.** `POST /api/v1/admin/restore` with a body of `{ "fileName": "money-....db" }`.
   The handler rejects anything that is not a plain file name inside the backups
   directory (no path separators, no traversal), opens the candidate read-only, and
   refuses it unless `PRAGMA integrity_check` returns `ok` **and** `__EFMigrationsHistory`
   exists and is non-empty. Only then does it copy the candidate to
   `<data dir>\restore-pending.db`. The live database is not touched.
3. **Apply.** At startup, *before* the first connection is opened, if
   `restore-pending.db` exists: back up the current `money.db` first, then delete
   `money.db`, `money.db-wal` and `money.db-shm`, and move the staged file into place.
   Migrations then run against the restored file as usual, so restoring an older backup
   upgrades it.

Restart: the restore endpoint calls `IHostApplicationLifetime.StopApplication()` after
staging. The desktop host closes its window on `ApplicationStopping` and relaunches
`Environment.ProcessPath` (Task 4). In server mode the operator restarts, which is
correct and needs no code.

Settings screen: a "Restore from a backup" card listing backups newest first (name,
date, size) with a per-row button, behind the existing `<dialog id="confirm">`
confirmation. Export and backup stay exactly as they are.

Tests (`Money.Application.Tests`, real SQLite plus a temp directory): staging accepts a
real backup; staging rejects a parent-directory path, a nested path, a missing file, a
non-SQLite file, and a valid SQLite file with no migrations history; staging does not
modify the live database; applying a pending restore replaces the database and clears
the staged file; applying with nothing staged is a no-op. Plus `Money.Api.Tests` cases
that the endpoint returns 404 for an unknown file name and 400 for a traversal attempt.

### Task 3 — Packaging and CI

- Application icon and product metadata on `Money.Desktop`: `ApplicationIcon`,
  `Product`, `Company`, `Version`, `FileVersion`.
- Trim the publish output. `DebugType=none` is already set, but referenced projects
  still emit pdb files; suppress those and the WebView2 xml doc files.
- Set `PublishTrimmed=false` **explicitly**, with the spec's reason in a comment, so
  nobody turns it on later.
- CI: add a `windows-latest` job that publishes `src/Money.Desktop` and runs a smoke
  test against the **published** executable — start it with a scratch
  `MONEYAPP_DATA_DIR`, poll `/health`, assert 200, stop it. This is spec section 14's
  named mitigation for "single-file publish breaks EF Core or Razor".
  The smoke test needs the app to start headless. `--server` already selects server
  mode, so the smoke test runs `Money.exe --server --urls http://127.0.0.1:5099` — and
  for that to work, `Money.Desktop` must open no window in server mode (Task 4).

### Task 4 — The host (`Money.Desktop/Program.cs`)

Owns every remaining gap, in one file:

- **Single instance** (G3): a named `Mutex`. If it is not acquired, find the existing
  window by its title, `SetForegroundWindow` it, and exit 0. Never a second Kestrel.
- **WebView2 Runtime check** (G1): `CoreWebView2Environment.GetAvailableBrowserVersionString()`
  before anything else. On failure, a `MessageBox` naming the Evergreen bootstrapper URL,
  then exit non-zero.
- **Fatal error dialog** (G2): the whole of `Main` inside a `try`/`catch` that shows the
  exception in a `MessageBox` rather than vanishing.
- **Server mode**: when `--server` is present, `app.RunAsync()` and no window.
- **Window state** (G4): read through Task 1's handler before creating the `Form`; apply
  the saved size and position only when the saved rectangle intersects a currently
  connected screen, otherwise fall back to the centred 1280x860 default. Save on close.
- **Restart after restore** (G5): register on `ApplicationStopping` to close the form,
  and after `Application.Run` returns, relaunch `Environment.ProcessPath` when the stop
  came from a restore rather than from the user closing the window.

### Task 5 — Documentation

`docs/using-the-app.md` and `docs/running-locally.md` both say restore does not exist
and tell the reader to copy files by hand. Both need rewriting against what ships.
`docs/using-the-app.md`'s stale "Not built yet" list needs the same pass.

## Out of scope

Server mode proper (phase 9): Dockerfile, cookie authentication, rate limiting.
`--server` here means "no window" and nothing more, which is what the CI smoke test
needs.

A truly single-file executable with `wwwroot` embedded as manifest resources. Publish
output today is `Money.exe` alongside four static files. Embedding them changes how
`Money.Api` registers its static file provider, affects both hosting modes, and buys
tidiness rather than function. Recorded here so it reads as a decision rather than an
oversight.
