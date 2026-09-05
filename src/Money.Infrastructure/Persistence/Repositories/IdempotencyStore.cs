using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyStore(MoneyDbContext context) : IIdempotencyStore
{
    // SQLite's own SQLITE_CONSTRAINT code, thrown when the (Key, Endpoint) primary key already
    // has a row - i.e. someone else reserved this key first.
    private const int SqliteConstraintErrorCode = 19;

    public async Task<IdempotencyReservation> TryReserveAsync(
        string key, string endpoint, string requestBodyHash, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        var reservation = new IdempotencyRecord
        {
            Key = key,
            Endpoint = endpoint,
            RequestBodyHash = requestBodyHash,
            ResponseJson = null,
            CreatedAtUtc = createdAtUtc
        };

        context.IdempotencyRecords.Add(reservation);

        try
        {
            // This must commit here, on its own, before the caller does anything else: reserving
            // and then saving together with the caller's later work would leave the same race
            // window the reserve-up-front design exists to close.
            await context.SaveChangesAsync(cancellationToken);
            return new IdempotencyReservation(Reserved: true, RequestBodyHash: null, ResponseJson: null);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            context.Entry(reservation).State = EntityState.Detached;

            var existing = await context.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint, cancellationToken);

            // existing is null only if the winning row was deleted between our failed insert and
            // this read, which this application never does (rows are only ever added or
            // completed in place); treat that impossible case the same as "still in progress".
            return new IdempotencyReservation(
                Reserved: false,
                RequestBodyHash: existing?.RequestBodyHash,
                ResponseJson: existing?.ResponseJson);
        }
    }

    public async Task CompleteAsync(
        string key, string endpoint, string responseJson, CancellationToken cancellationToken = default)
    {
        var record = await context.IdempotencyRecords
            .FirstAsync(r => r.Key == key && r.Endpoint == endpoint, cancellationToken);
        record.ResponseJson = responseJson;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        string key, string endpoint, CancellationToken cancellationToken = default)
    {
        var record = await context.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint, cancellationToken);
        if (record is null) return;

        context.IdempotencyRecords.Remove(record);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintErrorCode };
}
