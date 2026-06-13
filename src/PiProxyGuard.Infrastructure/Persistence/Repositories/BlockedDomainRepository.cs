using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;

namespace PiProxyGuard.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IBlockedDomainRepository"/>.</summary>
public class BlockedDomainRepository : Repository<BlockedDomain>, IBlockedDomainRepository
{
    public BlockedDomainRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<BlockedDomain>> SearchAsync(
        BlockSource? source, string? search, int count, CancellationToken cancellationToken = default)
    {
        var query = Set.AsQueryable();

        if (source is { } parsedSource)
        {
            query = query.Where(d => d.Source == parsedSource);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(d => d.Domain.Contains(search));
        }

        return await query
            .OrderBy(d => d.Domain)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<BlockedDomain?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(d => d.Domain == domain, cancellationToken);

    public async Task<IReadOnlyList<string>> GetActiveDomainNamesAsync(CancellationToken cancellationToken = default) =>
        await Set.Where(d => d.IsActive)
            .Select(d => d.Domain)
            .OrderBy(d => d)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, BlockedDomain>> GetFeedEntriesAsync(
        CancellationToken cancellationToken = default) =>
        await Set.Where(d => d.Source == BlockSource.Feed)
            .ToDictionaryAsync(d => d.Domain, cancellationToken);

    public async Task<IReadOnlyList<string>> GetNonFeedDomainNamesAsync(CancellationToken cancellationToken = default) =>
        await Set.Where(d => d.Source != BlockSource.Feed)
            .Select(d => d.Domain)
            .ToListAsync(cancellationToken);
}
