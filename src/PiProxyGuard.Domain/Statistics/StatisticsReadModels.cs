namespace PiProxyGuard.Domain.Statistics;

/// <summary>
/// Bucket size used by timeline aggregation.
/// </summary>
public enum TimelineInterval
{
    Hour = 0,
    Day = 1
}

/// <summary>
/// Aggregated traffic figures for a time range. Produced by the
/// repository layer and mapped to the API's <c>TrafficSummaryDto</c>.
/// </summary>
public sealed record TrafficSummary(
    long TotalRequests,
    long TotalBytes,
    long DeniedRequests,
    int UniqueClients,
    int UniqueHosts)
{
    public static TrafficSummary Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>Requests/bytes aggregated by destination host.</summary>
public sealed record HostTraffic(string Host, long Requests, long Bytes);

/// <summary>Requests/bytes/denied aggregated by client device.</summary>
public sealed record ClientTraffic(string ClientIp, long Requests, long Bytes, long DeniedRequests);

/// <summary>A single timeline bucket (per hour or per day).</summary>
public sealed record TimelineBucket(DateTime BucketStartUtc, long Requests, long Bytes);

/// <summary>HTTP status-code distribution row.</summary>
public sealed record StatusCodeCount(int StatusCode, long Count);

/// <summary>
/// HTTP method distribution (GET, POST, CONNECT, ...).
/// Surfaces the previously unused <see cref="Entities.ProxyLogEntry.Method"/>.
/// </summary>
public sealed record HttpMethodCount(string Method, long Requests, long Bytes);

/// <summary>
/// Response content-type distribution.
/// Surfaces the previously unused <see cref="Entities.ProxyLogEntry.ContentType"/>.
/// </summary>
public sealed record ContentTypeCount(string ContentType, long Requests, long Bytes);

/// <summary>
/// Squid result-code distribution (TCP_HIT, TCP_MISS, TCP_DENIED, ...).
/// Surfaces the previously unused <see cref="Entities.ProxyLogEntry.ResultCode"/>.
/// </summary>
public sealed record ResultCodeCount(string ResultCode, long Requests, long Bytes);

/// <summary>
/// Per-host latency statistics.
/// Surfaces the previously unused <see cref="Entities.ProxyLogEntry.ElapsedMs"/>.
/// </summary>
public sealed record HostPerformance(
    string Host,
    long Requests,
    double AverageElapsedMs,
    int MaxElapsedMs);

/// <summary>Per-client aggregates over a detection window.</summary>
public sealed record ClientWindowStat(string ClientIp, int Requests, long Bytes, int Denied);

/// <summary>A client that contacted a blocklisted domain within a window.</summary>
public sealed record BlockedDomainContact(string ClientIp, string Domain, int Count);
