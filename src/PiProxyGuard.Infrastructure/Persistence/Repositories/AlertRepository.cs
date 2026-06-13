using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IAlertRepository"/>.</summary>
public class AlertRepository : Repository<SuspiciousActivityAlert>, IAlertRepository
{
    public AlertRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IReadOnlyList<SuspiciousActivityAlert>> GetAlertsAsync(
        bool onlyUnacknowledged, int count, CancellationToken cancellationToken = default)
    {
        var query = Set.AsQueryable();
        if (onlyUnacknowledged)
        {
            query = query.Where(a => !a.IsAcknowledged);
        }

        return await query
            .OrderByDescending(a => a.DetectedAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlertKey>> GetRecentOpenAlertKeysAsync(
        DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        await Set.Where(a => a.DetectedAtUtc >= sinceUtc && !a.IsAcknowledged)
            .Select(a => new AlertKey(a.ClientIp, a.Type))
            .ToListAsync(cancellationToken);
}
