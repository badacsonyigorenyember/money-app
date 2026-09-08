# Bank sync (OTP via Enable Banking)

Read-only PSD2 account information for one account you link yourself. Free:
Enable Banking's *restricted production* mode costs nothing as long as the only
accounts the application can see are your own.

## One-time setup

1. Sign up at the Enable Banking Control Panel and **create an application**.
   Keep the PKCS#8 private key it generates — it is downloaded once.
2. Set the application to **production, restricted**, then
   **Activate by linking accounts** and link your OTP account. OTP's auth page
   asks you to pick *private* or *business* before the SCA step.
3. Note the **application id** and the linked account's **account uid**.
4. Point the app at all three. The private key never belongs in a committed
   file, so keep it on disk and reference the path:

   ```bash
   dotnet user-secrets set "EnableBanking:ApplicationId" "<application id>" --project src/Money.Api
   ```

   ```bash
   dotnet user-secrets set "EnableBanking:PrivateKeyPath" "C:\path\to\key.pem" --project src/Money.Api
   ```

   ```bash
   dotnet user-secrets set "EnableBanking:AccountUid" "<account uid>" --project src/Money.Api
   ```

   `EnableBanking:PrivateKeyPem` is accepted instead of `PrivateKeyPath` if you
   would rather inline the key. The key is read once at startup, so a rotated
   key needs a restart.

Nothing configured? The feed reports `bankfeed.not_configured` and the rest of
the app is unaffected.

## Using it

**Sync from bank** on the Transactions page, or:

```bash
curl -X POST http://localhost:5000/api/v1/import/bank -H "Content-Type: application/json" -d "{\"accountId\":\"<account guid>\"}"
```

Pressing it twice is safe. Every line carries the bank's `entry_reference` as
`ExternalRef`, a unique index enforces it, and a re-run reports
`alreadyPresent` instead of duplicating anything.

## What it does and does not do

| | |
|---|---|
| Window | Rolling **35 days**, not "this month" — card purchases book days late and a month boundary would drop them |
| Status | **Booked only.** A pending entry is re-issued under a new reference when it books, which would import it twice |
| Category | Everything lands on `Unclassified` (expense or income). Recategorise by editing the transaction |
| History | The bank exposes roughly **90 days**. Anything older has to be entered manually or come from an opening balance |
| Currency | Lines whose currency differs from the account's are counted and skipped, never converted |
| Voided | A line you removed stays removed; the next sync will not resurrect it |

## When it stops working

| Error | What happened |
|---|---|
| `bankfeed.session_expired` | The authorisation lapsed. Re-link the account in the Control Panel. PSD2 requires this roughly every 180 days (the EBA raised it from 90) |
| `bankfeed.rate_limited` | Unattended fetches are capped at about four a day. Wait, or fetch while you are present |
| `bankfeed.not_configured` | One of the three settings above is missing |
| `bankfeed.unavailable` | Network or timeout. Retry |

## Limits worth knowing

Two shortcuts are marked `ponytail:` in the code:

- **One `Unclassified` pair, in the imported account's currency.** A second
  account in a different currency will fail with
  `transaction.currency_mismatch_with_account`. Give the pair per-currency
  paths if that happens.
- **The hash fallback** (for an ASPSP that supplies no `entry_reference`) makes
  two genuinely identical same-day lines look like one, so the second is
  dropped. OTP does supply references, so this only matters if that changes.
