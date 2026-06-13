using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Statistics;

namespace PiProxyGuard.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IProxyLogRepository"/>. All
/// aggregations are translated to SQL (GROUP BY) and projected into
/// anonymous types, then mapped to compact read models — raw
/// <see cref="ProxyLogEntry"/> rows never leave the repository.
/// </summary>
public class ProxyLogRepository : Repository<ProxyLogEntry>, IProxyLogRepository
{
    public ProxyLogRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    private IQueryable<ProxyLogEntry> InRange(DateTime fromUtc, DateTime toUtc) =>
        Set.Where(e => e.TimestampUtc >= fromUtc && e.TimestampUtc < toUtc);

    public async Task<TrafficSummary> GetTrafficSummaryAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var query = InRange(fromUtc, toUtc);

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalRequests = g.LongCount(),
                TotalBytes = g.Sum(e => e.Bytes),
                DeniedRequests = g.LongCount(e => e.WasDenied)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (totals is null)
        {
            return TrafficSummary.Empty;
        }

        var uniqueClients = await query.Select(e => e.ClientIp).Distinct().CountAsync(cancellationToken);
        var uniqueHosts = await query.Where(e => e.Host != "")
            .Select(e => e.Host).Distinct().CountAsync(cancellationToken);

        return new TrafficSummary(
            totals.TotalRequests, totals.TotalBytes, totals.DeniedRequests, uniqueClients, uniqueHosts);
    }

    public async Task<IReadOnlyList<HostTraffic>> GetTopHostsAsync(
        DateTime fromUtc, DateTime toUtc, int count, CancellationToken cancellationToken = default)
    {
        var rows = await InRange(fromUtc, toUtc)
            .Where(e => e.Host != "")
            .GroupBy(e => e.Host)
            .Select(g => new { Host = g.Key, Requests = g.LongCount(), Bytes = g.Sum(e => e.Bytes) })
            .OrderByDescending(x => x.Requests)
            .Take(count)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new HostTraffic(x.Host, x.Requests, x.Bytes)).ToList();
    }

    public async Task<IReadOnlyList<ClientTraffic>> GetTopClientsAsync(
        DateTime fromUtc, DateTime toUtc, int count, CancellationToken cancellationToken = default)
    {
        var rows = await InRange(fromUtc, toUtc)
            .GroupBy(e => e.ClientIp)
            .Select(g => new
            {
                ClientIp = g.Key,
                Requests = g.LongCount(),
                Bytes = g.Sum(e => e.Bytes),
                Denied = g.LongCount(e => e.WasDenied)
            })
            .OrderByDescending(x => x.Bytes)
            .Take(count)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new ClientTraffic(x.ClientIp, x.Requests, x.Bytes, x.Denied)).ToList();
    }

    public async Task<IReadOnlyList<TimelineBucket>> GetTimelineAsync(
        DateTime fromUtc, DateTime toUtc, TimelineInterval interval, CancellationToken cancellationToken = default)
    {
        var byDay = interval == TimelineInterval.Day;

        var buckets = await InRange(fromUtc, toUtc)
            .GroupBy(e => new
            {
                e.TimestampUtc.Year,
                e.TimestampUtc.Month,
                e.TimestampUtc.Day,
                Hour = byDay ? 0 : e.TimestampUtc.Hour
            })
            .Select(g => new
            {
                g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour,
                Requests = g.LongCount(),
                Bytes = g.Sum(e => e.Bytes)
            })
            .ToListAsync(cancellationToken);

        return buckets
            .Select(b => new TimelineBucket(
                new DateTime(b.Year, b.Month, b.Day, b.Hour, 0, 0, DateTimeKind.Utc), b.Requests, b.Bytes))
            .OrderBy(p => p.BucketStartUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<StatusCodeCount>> GetStatusCodeBreakdownAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await InRange(fromUtc, toUtc)
            .GroupBy(e => e.StatusCode)
            .Select(g => new { StatusCode = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new StatusCodeCount(x.StatusCode, x.Count)).ToList();
    }

    public async Task<IReadOnlyList<HttpMethodCount>> GetMethodBreakdownAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await InRange(fromUtc, toUtc)
            .Where(e => e.Method != "")
            .GroupBy(e => e.Method)
            .Select(g => new { Method = g.Key, Requests = g.LongCount(), Bytes = g.Sum(e => e.Bytes) })
            .OrderByDescending(x => x.Requests)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new HttpMethodCount(x.Method, x.Requests, x.Bytes)).ToList();
    }

    public async Task<IReadOnlyList<ContentTypeCount>> GetContentTypeBreakdownAsync(
        DateTime fromUtc, DateTime toUtc, int count, CancellationToken cancellationToken = default)
    {
        var rows = await InRange(fromUtc, toUtc)
            .Where(e => e.ContentType != null && e.ContentType != "")
            .GroupBy(e => e.ContentType!)
            .Select(g => new { ContentType = g.Key, Requests = g.LongCount(), Bytes = g.Sum(e => e.Bytes) })
            .OrderByDescending(x => x.Requests)
            .Take(count)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new ContentTypeCount(x.ContentType, x.Requests, x.Bytes)).ToList();
    }

    public async Task<IReadOnlyList<ResultCodeCount>> GetResultCodeBreakdownAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await InRange(fromUtc, toUtc)
            .Where(e => e.ResultCode != "")
            .GroupBy(e => e.ResultCode)
            .Select(g => new { ResultCode = g.Key, Requests = g.LongCount(), Bytes = g.Sum(e => e.Bytes) })
            .OrderByDescending(x => x.Requests)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new ResultCodeCount(x.ResultCode, x.Requests, x.Bytes)).ToList();
    }

    public async Task<IReadOnlyList<HostPerformance>> GetSlowestHostsAsync(
        DateTime fromUtc, DateTime toUtc, int count, int minRequests, CancellationToken cancellationToken = default)
    {
        var floor = Math.Max(1, minRequests);

        var rows = await InRange(fromUtc, toUtc)
            .Where(e => e.Host != "")
            .GroupBy(e => e.Host)
            .Select(g => new
            {
                Host = g.Key,
                Requests = g.LongCount(),
                AverageElapsedMs = g.Average(e => (double)e.ElapsedMs),
                MaxElapsedMs = g.Max(e => e.ElapsedMs)
            })
            .Where(x => x.Requests >= floor)
            .OrderByDescending(x => x.AverageElapsedMs)
            .Take(count)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new HostPerformance(x.Host, x.Requests, x.AverageElapsedMs, x.MaxElapsedMs))
            .ToList();
    }

    public async Task<IReadOnlyList<ClientWindowStat>> GetClientWindowStatsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await InRange(fromUtc, toUtc)
            .GroupBy(e => e.ClientIp)
            .Select(g => new
            {
                ClientIp = g.Key,
                Requests = g.Count(),
                Bytes = g.Sum(e => e.Bytes),
                Denied = g.Count(e => e.WasDenied)
            })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new ClientWindowStat(x.ClientIp, x.Requests, x.Bytes, x.Denied)).ToList();
    }

    public async Task<IReadOnlyList<BlockedDomainContact>> GetBlockedDomainContactsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        // Join in SQL so the full blocklist is never loaded into memory.
        var rows = await InRange(fromUtc, toUtc)
            .Where(e => e.Host != "")
            .Join(
                DbContext.BlockedDomains.Where(d => d.IsActive),
                entry => entry.Host,
                blocked => blocked.Domain,
                (entry, blocked) => new { entry.ClientIp, blocked.Domain })
            .GroupBy(x => new { x.ClientIp, x.Domain })
            .Select(g => new { g.Key.ClientIp, g.Key.Domain, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new BlockedDomainContact(x.ClientIp, x.Domain, x.Count)).ToList();
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default) =>
        await Set.Where(e => e.TimestampUtc < cutoffUtc).ExecuteDeleteAsync(cancellationToken);
}
