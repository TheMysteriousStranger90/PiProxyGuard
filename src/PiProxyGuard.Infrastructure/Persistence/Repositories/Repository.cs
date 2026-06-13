using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Domain.Abstractions.Repositories;

namespace PiProxyGuard.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of the generic repository. Concrete repositories
/// derive from this and add their own query methods; the shared
/// <see cref="AppDbContext"/> is supplied by the <see cref="UnitOfWork"/>.
/// </summary>
public class Repository<TEntity> : IRepository<TEntity>
    where TEntity : class
{
    protected AppDbContext DbContext { get; }

    protected DbSet<TEntity> Set => DbContext.Set<TEntity>();

    public Repository(AppDbContext dbContext) => DbContext = dbContext;

    public async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync([id], cancellationToken);

    public void Add(TEntity entity) => Set.Add(entity);

    public void AddRange(IEnumerable<TEntity> entities) => Set.AddRange(entities);

    public void Remove(TEntity entity) => Set.Remove(entity);

    public void RemoveRange(IEnumerable<TEntity> entities) => Set.RemoveRange(entities);
}
