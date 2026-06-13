using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Statistics;

namespace PiProxyGuard.Domain.Abstractions.Repositories;

/// <summary>
/// Read/write access to parsed proxy log entries plus all of the traffic
/// aggregations the API and the detector need. Aggregations run as SQL
/// (GROUP BY) and return small read models, never raw <see cref="ProxyLogEntry"/>
/// rows, so callers stay free of EF Core and IQueryable concerns.
/// </summary>
public interface IProxyLogRepository : IRepository<ProxyLogEntry>
{
    /// <summary>Overall totals for the period.</summary>
    Task<TrafficSummary> GetTrafficSummaryAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Most requested destination hosts.</summary>
    Task<IReadOnlyList<HostTraffic>> GetTopHostsAsync(
        DateTime fromUtc, DateTime toUtc, int count, CancellationToken cancellationToken = default);

    /// <summary>Most active client devices by traffic volume.</summary>
    Task<IReadOnlyList<ClientTraffic>> GetTopClientsAsync(
        DateTime fromUtc, DateTime toUtc, int count, CancellationToken cancellationToken = default);

    /// <summary>Requests/bytes per hour or per day.</summary>
    Task<IReadOnlyList<TimelineBucket>> GetTimelineAsync(
        DateTime fromUtc, DateTime toUtc, TimelineInterval interval, CancellationToken cancellationToken = default);

    /// <summary>HTTP status-code distribution.</summary>
    Task<IReadOnlyList<StatusCodeCount>> GetStatusCodeBreakdownAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>HTTP method distribution (uses <see cref="ProxyLogEntry.Method"/>).</summary>
    Task<IReadOnlyList<HttpMethodCount>> GetMethodBreakdownAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Response content-type distribution (uses <see cref="ProxyLogEntry.ContentType"/>).</summary>
    Task<IReadOnlyList<ContentTypeCount>> GetContentTypeBreakdownAsync(
        DateTime fromUtc, DateTime toUtc, int count, CancellationToken cancellationToken = default);

    /// <summary>Squid result-code distribution (uses <see cref="ProxyLogEntry.ResultCode"/>).</summary>
    Task<IReadOnlyList<ResultCodeCount>> GetResultCodeBreakdownAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Slowest hosts by average latency (uses <see cref="ProxyLogEntry.ElapsedMs"/>).</summary>
    Task<IReadOnlyList<HostPerformance>> GetSlowestHostsAsync(
        DateTime fromUtc, DateTime toUtc, int count, int minRequests, CancellationToken cancellationToken = default);

    /// <summary>Per-client aggregates over a detection window.</summary>
    Task<IReadOnlyList<ClientWindowStat>> GetClientWindowStatsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Clients that contacted an active blocklisted domain within the window.</summary>
    Task<IReadOnlyList<BlockedDomainContact>> GetBlockedDomainContactsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Bulk-deletes entries older than the cutoff. Returns rows removed.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
    /// <summary>
    /// Distinct hosts each client contacted in the window with per-host counts.
    /// Feeds content-aware detectors (DGA / suspicious-domain).
    /// </summary>
    Task<IReadOnlyList<ClientHostContacts>> GetClientHostContactsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
