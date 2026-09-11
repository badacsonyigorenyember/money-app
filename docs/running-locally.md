# Running Money locally

## Prerequisites

- .NET 9 SDK

## Run the app

    dotnet run --project src/Money.Desktop

A window opens. There is no other way to run it — `Money.Api` is a library with no entry point,
so `dotnet run --project src/Money.Api` is refused. Kestrel binds a loopback port the OS picks
and the WebView2 window is its only client. On first run you are taken to a short setup wizard:
pick a currency, when your "month" starts, and your first account.

## Close the old window before opening a new build

One window is the limit — two processes writing one SQLite file is the failure the single-instance
guard exists to prevent. So if a window is already open, a newly started `Money.exe` says so and
closes; **it does not replace the one on screen.** Publishing while the old window is open and then
double-clicking the new exe therefore leaves you looking at the old build, running the old code
against the old schema, still showing whatever it was showing before.

## If the window shows an error instead of a page

A JSON block with a `traceId` and `"status": 500` means a page handler threw. The window has no
console, so the stack trace behind it goes to **`%APPDATA%\MoneyApp\money.log`** — written fresh
on every start, holding only the session you are looking at. Search it for `fail:`.

The commonest cause is a schema the running build does not match: an exe built after a migration,
opened on a database that has not had it applied yet (or the reverse — an older exe on a migrated
database). It reads as `SQLite Error 1: 'no such column: X'`. Publishing and reopening applies
whatever is pending, writing a pre-migration backup first.

## If the window looks unstyled

Black chart, Times New Roman, every tooltip showing at once: that is `app.css` coming back 404,
and the page says so — view source and the link reads `href="/app.css"` with no `?v=` stamp.

The cause is almost always a `dotnet build` or `dotnet test` **while the window is open**. Both
rewrite `src/Money.Desktop/bin/…/win-x64/wwwroot/`, and the running Kestrel serves 404 for the
seconds the file is missing. Nothing is broken and nothing needs fixing: close the window and
start it again. Build first, then run.

## Where your data lives

`%APPDATA%\MoneyApp\money.db` — deliberately **not** in the project folder. The project folder is
on the Desktop, which is commonly OneDrive-synced, and a synced SQLite file in WAL mode can be
corrupted by the sync client.

Override the location with the `MONEYAPP_DATA_DIR` environment variable, e.g. to run a second,
throwaway instance side by side with your real data:

    $env:MONEYAPP_DATA_DIR = "C:\temp\moneyapp-scratch"; dotnet run --project src/Money.Desktop

Backups are written to `<data dir>\backups\`. The Settings screen keeps the newest N of them
(configurable, default kept in Settings), oldest deleted first.

## Backup and restore

The Settings screen can create a backup on demand ("Back up now") and it writes a plain SQLite
file next to your data. It can also run an integrity check and export everything to JSON or CSV.

**Restore from a backup** is on the same screen: a list of the backups it can see, newest first,
with a button on each row. Picking one happens in three steps, because a live app cannot replace
the database file it has open — Microsoft.Data.Sqlite keeps pooled native handles past `Dispose`,
and WAL means three files rather than one.

1. **Validate.** The chosen file is opened read-only and has to pass `PRAGMA integrity_check` and
   carry a non-empty `__EFMigrationsHistory`. Anything else is refused, and nothing has happened
   yet. A file name with a path separator in it is refused before that.
2. **Stage.** The file is copied to `<data dir>\restore-pending.db` and the app stops itself. The
   live database is still untouched at this point.
3. **Swap, at the next start.** Before anything opens the database, the current `money.db` is
   backed up as `money-<timestamp>-before-restore.db`, then it and its `-wal` and `-shm` sidecars
   are deleted and the staged file is moved into place. Migrations then run as usual, so restoring
   an older backup upgrades it on the way in.

That pre-restore backup is an ordinary backup: it appears in the same list and counts against the
same retention limit. Restoring the wrong file is therefore not the one unrecoverable action in
the app — you can restore your way back out of it.

The app relaunches itself after staging, so from the outside a restore is a window that closes
and reopens.

### Restoring by hand

Still worth knowing, for the case where the app will not start at all and so cannot offer you the
button: **a backup is a real, complete SQLite database file.**

1. Close the app.
2. Find the backup you want in `<data dir>\backups\` — file names are timestamped, newest last.
3. Copy it over `<data dir>\money.db` (rename it to `money.db` first).
4. Delete `<data dir>\money.db-wal` and `money.db-shm` if they are there. They belong to the
   database you just replaced, and SQLite would otherwise try to recover them onto the new file.
5. Start the app again.

## Tests

    dotnet test

    dotnet test tests/Money.Domain.Tests
    dotnet test tests/Money.Architecture.Tests

    dotnet build -warnaserror

Mutation testing gates the domain project (line coverage is not accepted as evidence for money
logic):

    dotnet stryker --project Money.Domain.csproj

(run from `src/Money.Domain`) — mutation score must stay at or above 80.

## Adding a database migration

    dotnet ef migrations add <Name> --project src/Money.Infrastructure --output-dir Persistence/Migrations

Migrations are applied automatically at startup, after an automatic backup is taken.
