namespace PiProxyGuard.Domain.Abstractions.Repositories;

/// <summary>
/// Generic repository abstraction shared by every aggregate. Write
/// operations only stage changes; nothing is persisted until the owning
/// <see cref="IUnitOfWork.SaveChangesAsync"/> is called, so several
/// repositories can take part in a single atomic transaction.
/// </summary>
/// <typeparam name="TEntity">The aggregate/entity type.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>Finds an entity by its primary key, or null when missing.</summary>
    Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new entity for insertion.</summary>
    void Add(TEntity entity);

    /// <summary>Stages a batch of new entities for insertion.</summary>
    void AddRange(IEnumerable<TEntity> entities);

    /// <summary>Stages an entity for deletion.</summary>
    void Remove(TEntity entity);

    /// <summary>Stages a batch of entities for deletion.</summary>
    void RemoveRange(IEnumerable<TEntity> entities);
}
