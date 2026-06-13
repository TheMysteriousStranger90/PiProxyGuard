using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Statistics;

namespace PiProxyGuard.Api.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainCategorizer _categorizer;

    public StatsController(IUnitOfWork unitOfWork, IDomainCategorizer categorizer)
    {
        _unitOfWork = unitOfWork;
        _categorizer = categorizer;
    }

    /// <summary>Overall traffic summary for a period (defaults to the last 24 hours).</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<TrafficSummaryDto>> GetSummary(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var summary = await _unitOfWork.ProxyLogs.GetTrafficSummaryAsync(from, to, cancellationToken);

        return new TrafficSummaryDto(
            from, to,
            summary.TotalRequests,
            summary.TotalBytes,
            summary.DeniedRequests,
            summary.UniqueClients,
            summary.UniqueHosts);
    }

    /// <summary>Most requested hosts by request count.</summary>
    [HttpGet("top-hosts")]
    public async Task<ActionResult<List<TopHostDto>>> GetTopHosts(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int count = 20, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        count = Math.Clamp(count, 1, 200);

        var hosts = await _unitOfWork.ProxyLogs.GetTopHostsAsync(from, to, count, cancellationToken);
        return hosts.Select(x => new TopHostDto(x.Host, x.Requests, x.Bytes)).ToList();
    }

    /// <summary>Most active client devices by traffic volume.</summary>
    [HttpGet("top-clients")]
    public async Task<ActionResult<List<TopClientDto>>> GetTopClients(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int count = 20, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        count = Math.Clamp(count, 1, 200);

        var clients = await _unitOfWork.ProxyLogs.GetTopClientsAsync(from, to, count, cancellationToken);
        return clients.Select(x => new TopClientDto(x.ClientIp, x.Requests, x.Bytes, x.DeniedRequests)).ToList();
    }

    /// <summary>Requests and bytes per hour (or per day with interval=day).</summary>
    [HttpGet("timeline")]
    public async Task<ActionResult<List<TimelinePointDto>>> GetTimeline(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] string interval = "hour", CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var bucket = string.Equals(interval, "day", StringComparison.OrdinalIgnoreCase)
            ? TimelineInterval.Day
            : TimelineInterval.Hour;

        var buckets = await _unitOfWork.ProxyLogs.GetTimelineAsync(from, to, bucket, cancellationToken);
        return buckets.Select(b => new TimelinePointDto(b.BucketStartUtc, b.Requests, b.Bytes)).ToList();
    }

    /// <summary>Distribution of HTTP status codes for the period.</summary>
    [HttpGet("status-codes")]
    public async Task<ActionResult<List<StatusCodeDto>>> GetStatusCodes(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var codes = await _unitOfWork.ProxyLogs.GetStatusCodeBreakdownAsync(from, to, cancellationToken);
        return codes.Select(x => new StatusCodeDto(x.StatusCode, x.Count)).ToList();
    }

    /// <summary>Distribution of HTTP methods (GET, POST, CONNECT, ...) for the period.</summary>
    [HttpGet("methods")]
    public async Task<ActionResult<List<HttpMethodStatDto>>> GetMethods(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var methods = await _unitOfWork.ProxyLogs.GetMethodBreakdownAsync(from, to, cancellationToken);
        return methods.Select(x => new HttpMethodStatDto(x.Method, x.Requests, x.Bytes)).ToList();
    }

    /// <summary>Most common response content types (text/html, image/png, ...).</summary>
    [HttpGet("content-types")]
    public async Task<ActionResult<List<ContentTypeStatDto>>> GetContentTypes(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int count = 20, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        count = Math.Clamp(count, 1, 200);

        var types = await _unitOfWork.ProxyLogs.GetContentTypeBreakdownAsync(from, to, count, cancellationToken);
        return types.Select(x => new ContentTypeStatDto(x.ContentType, x.Requests, x.Bytes)).ToList();
    }

    /// <summary>Distribution of Squid result codes (TCP_HIT, TCP_MISS, TCP_DENIED, ...).</summary>
    [HttpGet("result-codes")]
    public async Task<ActionResult<List<ResultCodeStatDto>>> GetResultCodes(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var codes = await _unitOfWork.ProxyLogs.GetResultCodeBreakdownAsync(from, to, cancellationToken);
        return codes.Select(x => new ResultCodeStatDto(x.ResultCode, x.Requests, x.Bytes)).ToList();
    }

    /// <summary>Slowest hosts by average proxy latency (only hosts with enough requests).</summary>
    [HttpGet("performance")]
    public async Task<ActionResult<List<HostPerformanceDto>>> GetPerformance(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int count = 20, [FromQuery] int minRequests = 5,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        count = Math.Clamp(count, 1, 200);
        minRequests = Math.Clamp(minRequests, 1, 100_000);

        var hosts = await _unitOfWork.ProxyLogs.GetSlowestHostsAsync(from, to, count, minRequests, cancellationToken);
        return hosts
            .Select(x => new HostPerformanceDto(
                x.Host, x.Requests, Math.Round(x.AverageElapsedMs, 1), x.MaxElapsedMs))
            .ToList();
    }

    /// <summary>Log ingestion bookmarks — file path, byte offset and last-updated time.</summary>
    [HttpGet("ingestion")]
    public async Task<ActionResult<List<IngestionStatusDto>>> GetIngestionStatus(CancellationToken cancellationToken)
    {
        var states = await _unitOfWork.IngestionStates.GetAllAsync(cancellationToken);
        return states
            .Select(s => new IngestionStatusDto(
                s.FilePath, s.Offset, !string.IsNullOrEmpty(s.FirstLineFingerprint), s.UpdatedAtUtc))
            .ToList();
    }

    /// <summary>Traffic grouped by domain category (ads, tracking, social, streaming, CDN, malware).</summary>
    [HttpGet("categories")]
    public async Task<ActionResult<List<CategoryTrafficDto>>> GetCategories(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int sampleHosts = 500, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        sampleHosts = Math.Clamp(sampleHosts, 1, 2000);

        var hosts = await _unitOfWork.ProxyLogs.GetTopHostsAsync(from, to, sampleHosts, cancellationToken);
        return hosts
            .GroupBy(h => _categorizer.Categorize(h.Host))
            .Select(g => new CategoryTrafficDto(
                g.Key.ToString(),
                g.Sum(x => x.Requests),
                g.Sum(x => x.Bytes)))
            .OrderByDescending(c => c.Requests)
            .ToList();
    }

    private static (DateTime From, DateTime To) NormalizeRange(DateTime? fromUtc, DateTime? toUtc)
    {
        var to = toUtc ?? DateTime.UtcNow;
        var from = fromUtc ?? to.AddHours(-24);
        return from < to ? (from, to) : (to, from);
    }
}
