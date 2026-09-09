namespace Money.Infrastructure.Persistence;

/// <summary>
/// The table that de-duplicated retried POSTs on the JSON API. That API is gone - the desktop
/// window talks to page handlers - so nothing reads or writes these rows any more.
///
/// ponytail: kept because dropping it costs a migration against real ledgers for no gain. Delete
/// the class, its configuration and the DbSet next time a migration is being written anyway.
/// </summary>
public sealed class IdempotencyRecord
{
    public string Key { get; set; } = null!;
    public string Endpoint { get; set; } = null!;
    public string RequestBodyHash { get; set; } = null!;
    public string? ResponseJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
