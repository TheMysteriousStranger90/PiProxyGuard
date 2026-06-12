using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Infrastructure.Persistence;

namespace PiProxyGuard.Api.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public StatsController(AppDbContext dbContext) => _dbContext = dbContext;

    /// <summary>Overall traffic summary for a period (defaults to the last 24 hours).</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<TrafficSummaryDto>> GetSummary(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var query = _dbContext.LogEntries.Where(e => e.TimestampUtc >= from && e.TimestampUtc < to);

        var summary = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalRequests = g.LongCount(),
                TotalBytes = g.Sum(e => e.Bytes),
                DeniedRequests = g.LongCount(e => e.WasDenied)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var uniqueClients = await query.Select(e => e.ClientIp).Distinct().CountAsync(cancellationToken);
        var uniqueHosts = await query.Where(e => e.Host != "").Select(e => e.Host).Distinct().CountAsync(cancellationToken);

        return new TrafficSummaryDto(
            from, to,
            summary?.TotalRequests ?? 0,
            summary?.TotalBytes ?? 0,
            summary?.DeniedRequests ?? 0,
            uniqueClients,
            uniqueHosts);
    }

    /// <summary>Most requested hosts by request count.</summary>
    [HttpGet("top-hosts")]
    public async Task<ActionResult<List<TopHostDto>>> GetTopHosts(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int count = 20, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        count = Math.Clamp(count, 1, 200);

        var hosts = await _dbContext.LogEntries
            .Where(e => e.TimestampUtc >= from && e.TimestampUtc < to && e.Host != "")
            .GroupBy(e => e.Host)
            .Select(g => new { Host = g.Key, Requests = g.LongCount(), Bytes = g.Sum(e => e.Bytes) })
            .OrderByDescending(x => x.Requests)
            .Take(count)
            .ToListAsync(cancellationToken);

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

        var clients = await _dbContext.LogEntries
            .Where(e => e.TimestampUtc >= from && e.TimestampUtc < to)
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

        return clients.Select(x => new TopClientDto(x.ClientIp, x.Requests, x.Bytes, x.Denied)).ToList();
    }

    /// <summary>Requests and bytes per hour (or per day with interval=day).</summary>
    [HttpGet("timeline")]
    public async Task<ActionResult<List<TimelinePointDto>>> GetTimeline(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] string interval = "hour", CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);
        var byDay = string.Equals(interval, "day", StringComparison.OrdinalIgnoreCase);

        var buckets = await _dbContext.LogEntries
            .Where(e => e.TimestampUtc >= from && e.TimestampUtc < to)
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
            .Select(b => new TimelinePointDto(
                new DateTime(b.Year, b.Month, b.Day, b.Hour, 0, 0, DateTimeKind.Utc), b.Requests, b.Bytes))
            .OrderBy(p => p.BucketStartUtc)
            .ToList();
    }

    /// <summary>Distribution of HTTP status codes for the period.</summary>
    [HttpGet("status-codes")]
    public async Task<ActionResult<List<StatusCodeDto>>> GetStatusCodes(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var (from, to) = NormalizeRange(fromUtc, toUtc);

        var codes = await _dbContext.LogEntries
            .Where(e => e.TimestampUtc >= from && e.TimestampUtc < to)
            .GroupBy(e => e.StatusCode)
            .Select(g => new { StatusCode = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken);

        return codes.Select(x => new StatusCodeDto(x.StatusCode, x.Count)).ToList();
    }

    private static (DateTime From, DateTime To) NormalizeRange(DateTime? fromUtc, DateTime? toUtc)
    {
        var to = toUtc ?? DateTime.UtcNow;
        var from = fromUtc ?? to.AddHours(-24);
        return from < to ? (from, to) : (to, from);
    }
}
