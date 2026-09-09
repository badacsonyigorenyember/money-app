# Running Money locally

## Prerequisites

- .NET 9 SDK

## Run the app

    dotnet run --project src/Money.Desktop

A window opens. There is no other way to run it: Kestrel binds a loopback port the OS picks and
the WebView2 window is its only client. On first run you are taken to a short setup wizard: pick
a currency, when your "month" starts, and your first account.

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
