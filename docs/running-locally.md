# Running Money locally

## Prerequisites

- .NET 9 SDK

## Run the app

    dotnet run --project src/Money.Api

Open the URL printed in the console (`http://localhost:5247` by default). On first run you are
taken to a short setup wizard: pick a currency, when your "month" starts, and your first account.

## Where your data lives

`%APPDATA%\MoneyApp\money.db` — deliberately **not** in the project folder. The project folder is
on the Desktop, which is commonly OneDrive-synced, and a synced SQLite file in WAL mode can be
corrupted by the sync client.

Override the location with the `MONEYAPP_DATA_DIR` environment variable, e.g. to run a second,
throwaway instance side by side with your real data:

    MONEYAPP_DATA_DIR=C:\temp\moneyapp-scratch dotnet run --project src/Money.Api

Backups are written to `<data dir>\backups\`. The Settings screen keeps the newest N of them
(configurable, default kept in Settings), oldest deleted first.

## Backup and restore — restore is not built yet

The Settings screen can create a backup on demand ("Back up now") and it writes a plain SQLite
file next to your data. It can also run an integrity check and export everything to JSON or CSV.

There is deliberately **no restore button**. Restoring means stopping the running app, swapping
the live database file for a backup, and starting the app again — that only makes sense once
Money has a host process it actually controls and can restart, which arrives in phase 8 (the
WebView2 desktop shell). Doing it underneath a live `dotnet run` with an open WAL connection is
the single operation most likely to destroy your ledger, so it is not offered here.

You are not stranded if you need one now: **a backup is a real, complete SQLite database file.**
To restore by hand:

1. Close the app (stop `dotnet run`, or exit the desktop app once that exists).
2. Find the backup you want in `<data dir>\backups\` — file names are timestamped, newest last.
3. Copy it over `<data dir>\money.db` (rename it to `money.db` first).
4. Start the app again.

That is the whole procedure. If you ever lose data and there is no restore button in sight, this
is what to do instead.

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
