# Using Money — a walkthrough of everything the app does today

This is the tour, not the reference. [running-locally.md](running-locally.md) covers data
locations, backups and tests; [bank-sync.md](bank-sync.md) covers the Enable Banking setup.

Status note: what follows is what actually ships right now. Screens the spec promises but that do
not exist yet (dashboard, budgets, pockets, recurring, projections) are called out at the bottom
rather than quietly implied.

---

## 1. Start it

### The desktop app

```
dotnet publish src/Money.Desktop -c Release -o dist
```

Then run `dist\Money.exe`. It is a single self-contained file (~95 MB), so no .NET install is
needed on the machine that runs it. It does need the **WebView2 runtime**, which ships with
Windows 11 and with recent Edge — if the window opens blank, that is the thing to install.

What it does on launch:

- resolves the data directory (`%APPDATA%\MoneyApp` unless `MONEYAPP_DATA_DIR` is set),
- boots Kestrel on `http://127.0.0.1:0` — loopback only, on a port the OS picks, so it is never
  reachable from the network and never fights another instance for a fixed port,
- runs any pending migrations after taking an automatic backup,
- opens a 1280×860 WebView2 window pointed at that port.

Close the window and the server stops with it.

### Or the plain web app

```
dotnet run --project src/Money.Api
```

Open <http://localhost:5247>. Identical pipeline — both entry points call the same
`MoneyWebApp.CreateAsync`; the only difference is who hosts the window.

**Use this mode whenever you want the HTTP API**, because the desktop app's port is random and
changes every launch. Everything in section 8 assumes port 5247.

### A scratch instance to play in

```
$env:MONEYAPP_DATA_DIR = "C:\temp\moneyapp-scratch"; dotnet run --project src/Money.Api
```

Separate database, separate backups, your real ledger untouched. Delete the folder when done.

---

## 2. First run — the setup wizard

Any URL redirects to `/firstrun` until this is completed. Three fieldsets, then **Start**:

| Field | What it does |
|---|---|
| Currency | EUR / HUF / USD / GBP. This becomes the currency of your first account *and every category*. Settings will accept any ISO-4217 code later, but the accounts created here keep this one |
| Month starts | **On the 1st** (calendar month) or **On a payday** with a day 1–28. Days above 28 are not offered because not every month has them |
| Time zone | Defaults to `Europe/Budapest`. This decides what "today" means for a late-evening expense |
| Week starts on | Monday or Sunday |
| First account | Name, type (Bank or Cash only here — Savings and Investment come from the Accounts screen), starting balance, and the date it was that balance |
| Starter categories | Ticked by default. Creates 9 spending categories — Housing, Groceries, Eating out, Alcohol, Gaming, Transport, Health, Subscriptions, Other — **and 2 income ones**, Salary and Other income, which the checkbox label does not mention |

A non-zero starting balance is booked as a real opening-balance transaction against a hidden
`Equity / Opening balance` account. That account never shows up on the Accounts screen — it is
bookkeeping, not something you own.

The whole wizard is one database transaction: if the anchor day or currency is rejected, nothing
is written at all, and you get the error above the form rather than a half-built ledger.

You land on **Transactions**.

---

## 3. Transactions — `/transactions`

The main screen. Four things live here.

### Quick-add (spending only)

Date (defaults to today in *your* time zone), amount, category, account, optional description.
Press **Add** and the row appears without a page reload.

- The category dropdown lists your **expense** tree only, parents and children, children shown as
  `Parent → Child`.
- The account dropdown lists every non-archived asset account.
- The amount must be positive. Enter `20` for a €20 dinner — you never type a minus sign.

This is the only way to enter money *out* from the UI. Money *in* and money *between accounts*
are API-only today; see section 8.

### Sync from bank

Appears once you have at least one account. Pick the account, press **Sync from bank**.

Pulls the last **35 days** of **booked** lines from Enable Banking and files every one of them
under an auto-created `Unclassified` category. Pressing it twice is safe — each line carries the
bank's `entry_reference` and a unique index makes a re-run a no-op. You get a one-line summary
("Added 7 from the bank, filed under Unclassified (23 already here)").

Unconfigured, it reports `bankfeed.not_configured` and changes nothing. Setup is in
[bank-sync.md](bank-sync.md).

### Filters

Free-text search over description and payee, a from/to date range, and **Show removed**. Submit
with **Filter**. 50 rows per page — the API supports cursor paging, the page has no "next" button
yet, so narrow the date range to reach older rows.

### The list

Date, description, category, account, amount, and a **Remove** button.

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

Add form: name, type (**Bank**, **Cash**, **Savings**, **Investment**), optional starting balance
and as-of date. The table shows each account's live balance and an **Archive** button.

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

Four blocks:

**Preferences** — currency, when your month starts, week start, time zone, and how many backups to
keep. Changing the currency here does not re-denominate existing accounts.

Your month can start **on the 1st**, **on the first Monday**, or **on the day you get paid** (any
day from 1 to 28 — days above 28 are not offered, because not every month has them). This is the
boundary every monthly, quarterly and yearly total rolls over on.

**Backup** — *Back up now* writes a timestamped, complete SQLite file to
`%APPDATA%\MoneyApp\backups\`, oldest deleted first once you exceed the retention count. A backup
is also taken automatically before any migration at startup.

There is **no restore button**, on purpose. To restore: close the app, rename the backup you want
to `money.db`, copy it over `%APPDATA%\MoneyApp\money.db`, start the app again.

**Export** — JSON or CSV of the whole ledger, downloaded through the browser. Amounts export in
whole minor units (cents) so a spreadsheet cannot round them.

**Integrity** — *Check my data* verifies the invariants that matter: every non-voided transaction
balances to zero per currency, every account's balance matches its postings, no posting points at
an archived account. It reports "Everything checks out" or lists what it found. It is a read-only
check; it fixes nothing.

---

## 7. A five-minute run-through

1. `dotnet publish src/Money.Desktop -c Release -o dist`, then run `dist\Money.exe`.
2. Wizard: EUR, month starts on the 1st, `Europe/Budapest`, first account "Current account",
   Bank, starting balance `1200`, as-of today, starter categories ticked. **Start**.
3. On Transactions, add `20` / Groceries / Current account / "weekly shop". The row appears.
4. Add `9.99` / Subscriptions / Current account / "streaming".
5. Accounts → the balance reads `1,170.01`.
6. Categories → switch to Income; Salary and Other income are already there. Add a spending
   category "Coffee" under Eating out.
7. Transactions → search `shop`, then clear it. Press **Remove** on the streaming row and
   confirm; the balance goes back to `1,180.00`. Tick **Show removed** to see it greyed out.
8. Settings → **Check my data** (should be clean), then **Back up now**, then **Download CSV**.

---

## 8. What only the API can do right now

Run in web mode (`dotnet run --project src/Money.Api`, port 5247) for these. Full schema at
<http://localhost:5247/openapi/v1.json>. Get account and category ids from
`GET /api/v1/accounts` and `GET /api/v1/categories?kind=Income`.

### Record income

No UI for this yet. Use the generic transaction endpoint with two lines:

```bash
curl.exe -X POST http://localhost:5247/api/v1/transactions -H "Content-Type: application/json" -d "{\"occurredOn\":\"2026-09-08\",\"description\":\"September salary\",\"lines\":[{\"accountId\":\"<bank account id>\",\"amount\":3000},{\"accountId\":\"<Salary category id>\",\"amount\":3000}]}"
```

**Both amounts are positive.** You state amounts the way you would say them out loud; the sign
convention is applied per line from each account's kind. The same endpoint used for a manual
expense takes `+20` on the category and `-20` on the bank account.

### Move money between accounts

```bash
curl.exe -X POST http://localhost:5247/api/v1/transactions/transfer -H "Content-Type: application/json" -d "{\"amount\":500,\"fromAccountId\":\"<from>\",\"toAccountId\":\"<to>\",\"description\":\"To savings\"}"
```

A transfer touches two asset accounts and no expense category, so it can never appear as spending.

### Recategorise an imported line

Bank sync files everything under `Unclassified` and there is no edit screen yet. Replace the
transaction with the same shape pointing at the real category:

```bash
curl.exe -X PUT http://localhost:5247/api/v1/transactions/<id> -H "Content-Type: application/json" -d "{\"occurredOn\":\"2026-09-05\",\"description\":\"Tesco\",\"lines\":[{\"accountId\":\"<Groceries id>\",\"amount\":42.50},{\"accountId\":\"<bank account id>\",\"amount\":-42.50}]}"
```

This is the one genuinely awkward gap in day-to-day use if you turn bank sync on.

### Other endpoints

| | |
|---|---|
| `GET /api/v1/accounts/{id}/balance?asOf=YYYY-MM-DD` | balance on a past date |
| `GET /api/v1/transactions?from=&to=&q=&cursor=&limit=` | the paged list, cursor included |
| `POST /api/v1/transactions/{id}/void` | same as the Remove button |
| `PATCH /api/v1/accounts/{id}` | rename, re-parent, sort order, colour, icon, notes |
| `POST /api/v1/admin/backup`, `/integrity-check`, `GET /admin/export?format=` | the Settings buttons |
| `GET /health` | liveness |

The three creating endpoints (`/transactions`, `/transactions/quick-entry`,
`/transactions/transfer`) honour an `Idempotency-Key` header: send the same key twice and the
second call replays the first response instead of creating a duplicate.

---

## 9. Not built yet

So you do not go looking for them: the dashboard (`/` is a link list), budgets, virtual pockets,
interest accrual, recurring transactions, net-worth projections, an edit-transaction screen, a
rename button on Categories, a next-page button on Transactions, and restore-from-backup.

Everything above works on one machine, against one local file, with no account and no network —
except bank sync, which is the only thing that ever talks to the internet.
