using Money.Application.Abstractions;

namespace Money.Infrastructure.Persistence.Repositories;

public sealed class EfUnitOfWork(MoneyDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
