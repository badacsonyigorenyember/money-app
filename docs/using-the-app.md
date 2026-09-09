# Using Money — a walkthrough of everything the app does today

This is the tour, not the reference. [running-locally.md](running-locally.md) covers data
locations, backups and tests; [bank-sync.md](bank-sync.md) covers the Enable Banking setup.

Status note: what follows is what actually ships right now. Screens the spec promises but that do
not exist yet (budgets, pockets, interest and projections, a reports dashboard) are called out at
the bottom rather than quietly implied.

---

## 1. Start it

### The desktop app

```
.\publish.ps1
```

That is `dotnet publish src/Money.Desktop -c Release -o dist` with the flags remembered for you;
the script exists so the command in this document and the command CI runs cannot drift apart.
Then run `dist\Money.exe`.

Nine files land in `dist`, and the first one is nearly all of it:

| File | Size |
|---|---|
| `Money.exe` | 93 MB |
| `wwwroot\lib\htmx.min.js` | 50 KB |
| `Money.Api.deps.json` | 44 KB |
| `wwwroot\app.css` | 28 KB |
| `Money.Api.runtimeconfig.json`, `appsettings.json`, `appsettings.Development.json`, `wwwroot\manifest.webmanifest`, `wwwroot\sw.js` | under 1 KB each |

The exe is self-contained and compressed, so no .NET install is needed on the machine that runs
it. It is deliberately **not** trimmed — EF Core and Razor find things by reflection, and a
trimmed build fails at runtime in ways that are miserable to diagnose — which is where most of
those 93 MB come from. What is not there is clutter: no `.pdb`, no XML documentation files.

It does need the **WebView2 Runtime**, which ships with Windows 11 and with recent Edge. If it is
missing you get a dialog saying so and naming the Evergreen bootstrapper to install. The window
does not open blank and the process does not vanish without a word.

What it does on launch:

- resolves the data directory (`%APPDATA%\MoneyApp` unless `MONEYAPP_DATA_DIR` is set),
- checks the WebView2 Runtime is there,
- takes a lock named after that data directory, and exits immediately if another copy already
  holds it,
- boots Kestrel on `http://127.0.0.1:0` — loopback only, on a port the OS picks, so it is never
  reachable from the network and never fights another instance for a fixed port,
- runs any pending migrations after taking an automatic backup,
- opens a WebView2 window where you left it last time.

Close the window and the server stops with it.

### Living with the desktop app

Three things you notice, none of which have a setting:

- **It remembers its window.** Size and position are written down when you close it and used
  again next time; the first run gets a centred 1280×860. If the screen it was on has since been
  unplugged, it comes back centred at the default rather than off the edge of the world. The
  rectangle lives in the same settings row as everything else and is not editable from Settings —
  you move the window, that is the setting.
- **One copy at a time, per data directory.** Double-click it twice and the second copy exits
  without a word, because two processes writing one SQLite file is how you lose a ledger. The
  lock is named after the data directory rather than the machine, so a scratch instance under a
  different `MONEYAPP_DATA_DIR` still opens happily alongside your real one.
- **It says when something is wrong.** A packaged Windows app has no console, so an unhandled
  error would otherwise be a process that simply disappears. Every failure gets a message box
  instead — the missing runtime above, and anything unexpected after that.

One more: a restore closes the window and opens it again by itself. See
[section 6](#6-settings--settings).

### Or the plain web app

```
dotnet run --project src/Money.Api
```

Open <http://localhost:5247>. Identical pipeline — both entry points call the same
`MoneyWebApp.CreateAsync`; the only difference is who hosts the window.

The published exe can do this too:

```
dist\Money.exe --server --urls http://127.0.0.1:5099
```

That runs the same app headless — no window, no WebView2 check, no single-instance lock — which
is how CI smoke-tests the published executable against `/health` on a runner with no browser
runtime at all. `--server` means "no window" and nothing more; server mode proper (a Dockerfile,
sign-in, rate limiting) is phase 9 and is not here.

**Use one of these two modes whenever you want the HTTP API**, because the desktop app's port is
random and changes every launch. Everything in section 8 assumes port 5247.

### A scratch instance to play in

```
$env:MONEYAPP_DATA_DIR = "C:\temp\moneyapp-scratch"; dotnet run --project src/Money.Api
```

Separate database, separate backups, your real ledger untouched. Delete the folder when done.

---

## 2. First run — the setup wizard

Any URL redirects to `/firstrun` until this is completed. A handful of fieldsets, then **Start**:

| Field | What it does |
|---|---|
| Currency | Any currency the app knows: CHF, CZK, DKK, EUR, GBP, HUF, JPY, NOK, PLN, RON, SEK, USD. This becomes the currency of your first account *and every category*. Later accounts can be kept in any of the others |
| Month starts | **On the 1st** (calendar month), **on the first Monday**, or **on a payday** with a day 1–28. Days above 28 are not offered because not every month has them |
| Week starts on | Any day of the week; Monday by default |
| Time zone | Defaults to `Europe/Budapest`. This decides what "today" means for a late-evening expense |
| First account | Name, type (Bank or Cash only here — Savings and Investment come from the Accounts screen), starting balance, and the date it was that balance |
| Starter categories | Ticked by default. Creates 9 spending categories — Housing, Groceries, Eating out, Alcohol, Gaming, Transport, Health, Subscriptions, Other — **and 2 income ones**, Salary and Other income, which the checkbox label does not mention |

A non-zero starting balance is booked as a real opening-balance transaction against a hidden
`Equity / Opening balance` account. That account never shows up on the Accounts screen — it is
bookkeeping, not something you own.

The whole wizard is one database transaction: if the anchor day or currency is rejected, nothing
is written at all, and you get the error above the form rather than a half-built ledger.

You land on **Home**.

---

## 3. Home — `/transactions`

The main screen, and where `/` sends you. One month at a time, and everything on the page is
about that same month.

### Stepping through the months

Arrows either side of the month's name move back and forward. The forward arrow is greyed out on
the month in progress, because there is nothing after it to look at. Your search text and the
"show removed" tick travel with you between months.

Which dates "September" covers depends on the anchor you chose in Settings: on a payday anchor a
month runs payday to payday, and it is still named after the calendar month it opens in.

### Where you stand

A chart of the month: one line per account, day by day, starting from what that account carried
in from the month before. The carried-in amount is also drawn as a dotted rule straight across,
so "am I ahead of where I started" is a glance rather than a subtraction. A month in progress
stops at today rather than drawing a flat line into the future.

The chips above it choose which accounts are drawn; ticking one re-asks the server rather than
hiding a line in the browser, because the accounts share a vertical scale.

Below the chart, the same month as figures: **Started, In, Out, Kept, Now** for every account,
each in its own currency.

The chart is drawn by hand in SVG rather than by a charting library, so it looks like itself with
the network switched off.

### Accounts in other currencies

The chart has one vertical scale, so an account kept in another currency is converted to your
base currency for the drawing only — the figures table underneath, and the balance on the
Accounts screen, still say what the account itself holds.

Rates come from [Frankfurter](https://github.com/lineofflight/frankfurter), which republishes the ECB's daily
reference rates and needs no key or account. They are cached, and a fetch that fails is not an
error: the account keeps its row in the table, its chip is greyed out with the reason, and it
stays off the chart rather than being drawn at a rate nobody has.

### Recording something

The **+** button in the bottom corner opens one sheet for everything you can record: date
(defaults to today in *your* time zone), amount, category, account, optional description.

- The category list is in two groups, **Money out** and **Money in**. Picking an income category
  is how you record money coming in — there is no separate income form and no minus sign
  anywhere.
- The amount is read in the currency of the account you pick. An account outside your base
  currency has its code beside its name in that dropdown, because `2500` means very different
  things in euros and forints.
- The amount is always positive.
- Ticking **Repeat this automatically** turns the same entry into a schedule; see below.

The row appears without a page reload, and the chart above it moves with it.

### Repeating

Anything you ticked "repeat" on shows up here: what it is, when it repeats, when it next falls
due, and the amount. Each has **Pause**/**Resume** and **Stop**.

Schedules can be daily, weekly, monthly or yearly with an interval ("every 2 weeks"), or a custom
number of years, months and days. Monthly and yearly ones take either a day of the month or a
weekday of the month ("the last Friday"), and the 31st becomes the 28th or 29th in February. An
end date is optional.

Entries are made when they fall due — at startup, and again every time you open this page.
Running it twice posts nothing extra: a unique index in the database, not just a check in code,
is what makes that true. **Stop** ends the schedule and forgets it; the entries it already made
stay, because they are history, and history is voided one entry at a time.

### Sync from bank

Appears once you have at least one account. Pick the account, press **Sync from bank**.

Pulls the last **35 days** of **booked** lines from Enable Banking and files every one of them
under an auto-created `Unclassified` category. Pressing it twice is safe — each line carries the
bank's `entry_reference` and a unique index makes a re-run a no-op. You get a one-line summary
("Added 7 from the bank, filed under Unclassified (23 already here)"), including a count of any
lines skipped for being in another currency than the account.

Unconfigured, it reports `bankfeed.not_configured` and changes nothing. Setup is in
[bank-sync.md](bank-sync.md).

### The list

Free-text search over description and payee, and **Show removed**. The month is the filter;
there are no date boxes, because the arrows above are the date control.

Date, description, category, account, amount, and a **Remove** button, newest first. Up to 200
entries — the API pages by cursor, the page has no "next" button, so a month with more than 200
entries is cut off at 200.

**Remove voids, it does not delete.** The transaction stays in the database marked voided with a
reason, stops counting toward every balance and report, and reappears struck through when you tick
"Show removed". This is deliberate: a transaction is never hard-deleted, not even when the account
or category it names is.

Amounts are shown the way a human expects — an income row reads positive even though the ledger
stores it as a credit. That flip happens in exactly one mapper.

---

## 4. Categories — `/categories`

Categories *are* accounts in the same tree, which is what makes transfers structurally impossible
to report as spending.

- The dropdown at the top switches between **Spending** and **Income** trees.
- The add form takes a name and a parent. Only top-level categories are offered as parents, so
  the UI gives you two levels; deeper nesting is possible through the API.
- Each node has **Archive**. An archived category can no longer receive new postings, but every
  past transaction keeps it.
- Archived categories are listed below the tree with a **Delete for good** button, which works
  exactly as it does for accounts — see [section 5](#5-accounts--accounts).
- A child always inherits its parent's kind — you cannot hang a spending category under an income
  one.

There is no rename button on this screen yet, though the handler behind it exists. Rename via
`PATCH /api/v1/accounts/{id}` for now.

---

## 5. Accounts — `/accounts`

Add form: name, type (**Bank**, **Cash**, **Savings**, **Investment**), currency, optional
starting balance and as-of date. The currency defaults to your base one and is what the account
is actually kept in; the table shows each account's live balance in that currency, with an
**Archive** button beside it.

### Archived accounts, and deleting one

Archived accounts get their own table below the open ones. Each row says what the ledger still
holds against it — either **Nothing uses it** or a count of entries — and carries a **Delete for
good** button. There is no undo, and everything filed underneath goes with it.

What deleting does depends on that count:

- **Nothing uses it.** The account is erased from the database. It is as if you had never created
  it, and its name is free to use again.
- **It has entries.** The row is kept, but only so your history can still name it. The account
  disappears from every list, picker and from the archive itself, its name is free to use again,
  and old entries show it as `Groceries (deleted)`.

Your transactions are never touched either way. Deleting a category has never been a way to
delete spending — remove the transactions themselves if that is what you want.

Every balance is a `SUM` over that account's non-voided postings, computed on read. There is no
stored balance column to drift out of date.

The hidden `Equity / Opening balance` account is filtered out here on purpose, along with all
your categories — this screen shows only the things you would call an account.

---

## 6. Settings — `/settings`

**Preferences** — currency, when your month starts, week start, time zone, and how many backups to
keep. Changing the currency here does not re-denominate existing accounts; it changes what you are
shown in, and what other currencies are converted to on the chart.

Your month can start **on the 1st**, **on the first Monday**, or **on the day you get paid** (any
day from 1 to 28 — days above 28 are not offered, because not every month has them). This is the
boundary every monthly, quarterly and yearly total rolls over on.

**Back up now** — writes a timestamped, complete SQLite file to `%APPDATA%\MoneyApp\backups\`,
oldest deleted first once you exceed the retention count. A backup is also taken automatically
before any migration at startup.

**Restore from a backup** — the backups it can see, newest first, with a button on each row.
Restoring replaces everything you have now with what was in that file, so anything recorded since
is gone from the app.

It is not, however, the one unrecoverable thing in the app. Your current data is backed up first,
that backup appears in the same list, and you can restore your way back out of a restore you did
not mean. The chosen file is also checked before anything happens: it has to open as SQLite, pass
an integrity check and carry a migration history.

The app closes to finish the job, because a running app cannot replace the database file it has
open — the swap happens at the next start, before anything opens the database. The desktop app
relaunches itself, so from the outside a restore is a window that closes and reopens; in web mode
the process exits and you start it again yourself. [running-locally.md](running-locally.md#backup-and-restore)
has the long version, including how to restore by hand when the app will not start at all.

**Export** — JSON or CSV of the whole ledger, downloaded through the browser. Amounts export in
whole minor units (cents) so a spreadsheet cannot round them.

**Check my data** — verifies the invariants that matter: every non-voided transaction balances to
zero per currency, every account's balance matches its postings, no posting points at an archived
account. It reports "Everything checks out" or lists what it found. It is a read-only check; it
fixes nothing.

---

## 7. A five-minute run-through

1. `.\publish.ps1`, then run `dist\Money.exe`.
2. Wizard: EUR, month starts on the 1st, `Europe/Budapest`, first account "Current account",
   Bank, starting balance `1200`, as-of today, starter categories ticked. **Start**.
3. On Home, press **+** and add `20` / Groceries / Current account / "weekly shop". The row
   appears and the chart moves.
4. Press **+** again, pick **Salary** under *Money in*, `3000`, and tick **Repeat this
   automatically** — monthly, on today's date. It posts once now and says when it lands next.
5. Accounts → the balance reads `4,180.00`.
6. Categories → switch to Income; Salary and Other income are already there. Add a spending
   category "Coffee" under Eating out.
7. Home → search `shop`, then clear it. Press **Remove** on the groceries row and confirm; the
   balance goes back to `4,200.00`. Tick **Show removed** to see it struck through.
8. Step back a month with the left arrow: empty, as it should be. Step forward again.
9. Settings → **Check my data** (should be clean), then **Back up now** — the backup shows up in
   the restore list underneath — then **Download CSV**.

---

## 8. What only the API can do right now

Run in web mode (`dotnet run --project src/Money.Api`, port 5247) for these. Full schema at
<http://localhost:5247/openapi/v1.json>. Get account and category ids from
`GET /api/v1/accounts` and `GET /api/v1/categories?kind=Income`.

### Move money between accounts

```bash
curl.exe -X POST http://localhost:5247/api/v1/transactions/transfer -H "Content-Type: application/json" -d "{\"amount\":500,\"fromAccountId\":\"<from>\",\"toAccountId\":\"<to>\",\"description\":\"To savings\"}"
```

A transfer touches two asset accounts and no expense category, so it can never appear as spending.
There is no screen for it yet.

### Recategorise an imported line

Bank sync files everything under `Unclassified` and there is no edit screen yet. Replace the
transaction with the same shape pointing at the real category:

```bash
curl.exe -X PUT http://localhost:5247/api/v1/transactions/<id> -H "Content-Type: application/json" -d "{\"occurredOn\":\"2026-09-05\",\"description\":\"Tesco\",\"lines\":[{\"accountId\":\"<Groceries id>\",\"amount\":42.50},{\"accountId\":\"<bank account id>\",\"amount\":-42.50}]}"
```

This is the one genuinely awkward gap in day-to-day use if you turn bank sync on.

### A transaction with more than two lines

The generic endpoint takes any number of lines, which is the only way to record something split
across several categories:

```bash
curl.exe -X POST http://localhost:5247/api/v1/transactions -H "Content-Type: application/json" -d "{\"occurredOn\":\"2026-09-08\",\"description\":\"September salary\",\"lines\":[{\"accountId\":\"<bank account id>\",\"amount\":3000},{\"accountId\":\"<Salary category id>\",\"amount\":3000}]}"
```

**Both amounts there are positive.** You state amounts the way you would say them out loud; the
sign convention is applied per line from each account's kind.

### Other endpoints

| | |
|---|---|
| `GET /api/v1/accounts/{id}/balance?asOf=YYYY-MM-DD` | balance on a past date |
| `GET /api/v1/transactions?from=&to=&q=&cursor=&limit=` | the paged list, cursor included |
| `POST /api/v1/transactions/{id}/void` | same as the Remove button |
| `PATCH /api/v1/accounts/{id}` | rename, re-parent, sort order, colour, icon, notes |
| `GET/POST /api/v1/recurring-rules`, `/{id}/pause`, `/{id}/resume`, `DELETE /{id}` | the Repeating section |
| `POST /api/v1/recurring/run` | post whatever is due now |
| `POST /api/v1/import/bank` | the Sync from bank button |
| `POST /api/v1/admin/backup`, `GET /admin/backups`, `POST /admin/restore` | the backup and restore buttons |
| `POST /api/v1/admin/integrity-check`, `GET /admin/export?format=` | the other Settings buttons |
| `GET /health` | liveness |

The four creating endpoints (`/transactions`, `/transactions/quick-entry`,
`/transactions/transfer`, `/recurring-rules`) honour an `Idempotency-Key` header: send the same key
twice and the second call replays the first response instead of creating a duplicate.

---

## 9. Not built yet

So you do not go looking for them: budgets, virtual pockets, interest accrual, net-worth
projections, a reports dashboard, an edit-transaction screen, a transfer screen, a rename button
on Categories, and any way to see past the 200th entry in a month.

Everything above works on one machine, against one local file, with no account and no sign-in.
Two things reach the internet, both of them optional and both of them failing quietly when it is
not there: bank sync, and the exchange rates that let accounts in different currencies share one
chart.
