namespace Money.Domain.Ledger;

/// <summary>
/// Where a transaction came from. Recurring is used in phase 3, Accrual in phase 6. Import is
/// written by nothing since bank sync was removed; it stays so rows an older build imported
/// still read back, and so a future CSV import needs no migration.
/// </summary>
public enum TransactionSourceKind
{
    Manual = 1,
    Recurring = 2,
    Accrual = 3,
    Import = 4
}
