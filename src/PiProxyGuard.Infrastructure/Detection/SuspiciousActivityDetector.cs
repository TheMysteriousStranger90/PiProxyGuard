using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Persistence;

namespace PiProxyGuard.Infrastructure.Detection;

/// <summary>
/// Analyzes a sliding window of log entries and raises alerts for clients
/// that behave suspiciously: request floods, traffic spikes, repeated
/// denied requests and contacts with blocklisted domains.
/// </summary>
public class SuspiciousActivityDetector
{
    private readonly AppDbContext _dbContext;
    private readonly DetectionOptions _options;
    private readonly ILogger<SuspiciousActivityDetector> _logger;

    public SuspiciousActivityDetector(
        AppDbContext dbContext,
        IOptions<DetectionOptions> options,
        ILogger<SuspiciousActivityDetector> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> AnalyzeAsync(DateTime windowEndUtc, CancellationToken cancellationToken)
    {
        var windowStartUtc = windowEndUtc.AddMinutes(-_options.WindowMinutes);

        var perClient = await _dbContext.LogEntries
            .Where(e => e.TimestampUtc >= windowStartUtc && e.TimestampUtc < windowEndUtc)
            .GroupBy(e => e.ClientIp)
            .Select(g => new
            {
                ClientIp = g.Key,
                Requests = g.Count(),
                Bytes = g.Sum(e => e.Bytes),
                Denied = g.Count(e => e.WasDenied)
            })
            .ToListAsync(cancellationToken);

        var alerts = new List<SuspiciousActivityAlert>();

        foreach (var client in perClient)
        {
            if (client.Requests > _options.MaxRequestsPerWindow)
            {
                alerts.Add(CreateAlert(client.ClientIp, AlertType.HighRequestRate, windowStartUtc, windowEndUtc,
                    $"{client.Requests} requests in {_options.WindowMinutes} min (threshold {_options.MaxRequestsPerWindow})"));
            }

            if (client.Bytes > _options.MaxBytesPerWindow)
            {
                alerts.Add(CreateAlert(client.ClientIp, AlertType.HighTrafficVolume, windowStartUtc, windowEndUtc,
                    $"{client.Bytes / (1024 * 1024)} MB in {_options.WindowMinutes} min (threshold {_options.MaxBytesPerWindow / (1024 * 1024)} MB)"));
            }

            if (client.Denied > _options.MaxDeniedPerWindow)
            {
                alerts.Add(CreateAlert(client.ClientIp, AlertType.RepeatedDeniedRequests, windowStartUtc, windowEndUtc,
                    $"{client.Denied} denied requests in {_options.WindowMinutes} min (threshold {_options.MaxDeniedPerWindow})"));
            }
        }

        alerts.AddRange(await DetectBlockedDomainContactsAsync(windowStartUtc, windowEndUtc, cancellationToken));

        var newAlerts = await FilterDuplicatesAsync(alerts, cancellationToken);
        if (newAlerts.Count > 0)
        {
            _dbContext.Alerts.AddRange(newAlerts);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Raised {Count} suspicious-activity alerts", newAlerts.Count);
        }

        return newAlerts.Count;
    }

    private async Task<List<SuspiciousActivityAlert>> DetectBlockedDomainContactsAsync(
        DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken)
    {
        // Join in SQL so we never load the full blocklist into memory.
        var contacts = await _dbContext.LogEntries
            .Where(e => e.TimestampUtc >= windowStartUtc && e.TimestampUtc < windowEndUtc && e.Host != "")
            .Join(
                _dbContext.BlockedDomains.Where(d => d.IsActive),
                entry => entry.Host,
                blocked => blocked.Domain,
                (entry, blocked) => new { entry.ClientIp, blocked.Domain })
            .GroupBy(x => new { x.ClientIp, x.Domain })
            .Select(g => new { g.Key.ClientIp, g.Key.Domain, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return contacts
            .Select(c => CreateAlert(c.ClientIp, AlertType.BlockedDomainContact, windowStartUtc, windowEndUtc,
                $"Contacted blocklisted domain {c.Domain} ({c.Count}x)"))
            .ToList();
    }

    private async Task<List<SuspiciousActivityAlert>> FilterDuplicatesAsync(
        List<SuspiciousActivityAlert> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        // Suppress repeats: skip an alert when the same client already has an
        // unacknowledged alert of the same type within the last hour.
        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await _dbContext.Alerts
            .Where(a => a.DetectedAtUtc >= since && !a.IsAcknowledged)
            .Select(a => new { a.ClientIp, a.Type })
            .ToListAsync(cancellationToken);

        var recentKeys = recent.Select(r => (r.ClientIp, r.Type)).ToHashSet();
        return candidates.Where(c => !recentKeys.Contains((c.ClientIp, c.Type))).ToList();
    }

    private static SuspiciousActivityAlert CreateAlert(
        string clientIp, AlertType type, DateTime windowStartUtc, DateTime windowEndUtc, string description) =>
        new()
        {
            ClientIp = clientIp,
            Type = type,
            Description = description,
            WindowStartUtc = windowStartUtc,
            WindowEndUtc = windowEndUtc,
            DetectedAtUtc = DateTime.UtcNow
        };
}
