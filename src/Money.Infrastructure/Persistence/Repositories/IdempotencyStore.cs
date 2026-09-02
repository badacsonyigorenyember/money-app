using Microsoft.EntityFrameworkCore;
using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyStore(MoneyDbContext context) : IIdempotencyStore
{
    public async Task<string?> TryGetResponseAsync(
        string key, string endpoint, CancellationToken cancellationToken = default) =>
        (await context.IdempotencyRecords
                      .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint, cancellationToken))
        ?.ResponseJson;

    public Task RecordAsync(
        string key, string endpoint, string responseJson, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key,
            Endpoint = endpoint,
            ResponseJson = responseJson,
            CreatedAtUtc = createdAtUtc
        });

        return Task.CompletedTask;
    }
}
