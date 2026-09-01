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
