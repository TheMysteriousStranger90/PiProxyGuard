using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="ITunneledDomainRepository"/>.</summary>
public class TunneledDomainRepository : Repository<TunneledDomain>, ITunneledDomainRepository
{
    public TunneledDomainRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<TunneledDomain>> SearchAsync(
        string? search, int count, CancellationToken cancellationToken = default)
    {
        var query = Set.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(d => d.Domain.Contains(search));
        }

        return await query
            .OrderBy(d => d.Domain)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<TunneledDomain?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(d => d.Domain == domain, cancellationToken);

    public async Task<IReadOnlyList<string>> GetAllDomainNamesAsync(CancellationToken cancellationToken = default) =>
        await Set.Select(d => d.Domain).OrderBy(d => d).ToListAsync(cancellationToken);
}
