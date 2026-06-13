using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Infrastructure.Detection;

/// <summary>
/// Analyzes a sliding window of log entries and raises alerts for clients that
/// behave suspiciously: request floods, traffic spikes (vs. the client's own
/// baseline), repeated denied requests, contacts with blocklisted domains and
/// contacts with algorithmically-generated (DGA-style) domains. Thresholds can
/// be tuned per client via profiles. Suspicious domains can be auto-blocked
/// with a TTL, and new alerts are pushed to the notification channels. Reads
/// and writes go through <see cref="IUnitOfWork"/>.
/// </summary>
public class SuspiciousActivityDetector
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DetectionOptions _options;
    private readonly ClientProfileResolver _profiles;
    private readonly INotificationDispatcher _notifications;
    private readonly ILogger<SuspiciousActivityDetector> _logger;

    public SuspiciousActivityDetector(
        IUnitOfWork unitOfWork,
        IOptions<DetectionOptions> options,
        ClientProfileResolver profiles,
        INotificationDispatcher notifications,
        ILogger<SuspiciousActivityDetector> logger)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _profiles = profiles;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<int> AnalyzeAsync(DateTime windowEndUtc, CancellationToken cancellationToken)
    {
        var windowStartUtc = windowEndUtc.AddMinutes(-_options.WindowMinutes);

        var perClient = await _unitOfWork.ProxyLogs
            .GetClientWindowStatsAsync(windowStartUtc, windowEndUtc, cancellationToken);

        var alerts = new List<SuspiciousActivityAlert>();

        foreach (var client in perClient)
        {
            var t = _profiles.Resolve(client.ClientIp);
            var who = t.ProfileName is null ? string.Empty : $" [{t.ProfileName}]";

            if (client.Requests > t.MaxRequestsPerWindow)
            {
                alerts.Add(CreateAlert(client.ClientIp, AlertType.HighRequestRate, windowStartUtc, windowEndUtc,
                    $"{client.Requests} requests in {_options.WindowMinutes} min (threshold {t.MaxRequestsPerWindow}){who}"));
            }

            if (client.Bytes > t.MaxBytesPerWindow)
            {
                alerts.Add(CreateAlert(client.ClientIp, AlertType.HighTrafficVolume, windowStartUtc, windowEndUtc,
                    $"{client.Bytes / (1024 * 1024)} MB in {_options.WindowMinutes} min (threshold {t.MaxBytesPerWindow / (1024 * 1024)} MB){who}"));
            }

            if (client.Denied > t.MaxDeniedPerWindow)
            {
                alerts.Add(CreateAlert(client.ClientIp, AlertType.RepeatedDeniedRequests, windowStartUtc, windowEndUtc,
                    $"{client.Denied} denied requests in {_options.WindowMinutes} min (threshold {t.MaxDeniedPerWindow}){who}"));
            }
        }

        if (_options.DetectTrafficSpikes)
        {
            alerts.AddRange(await DetectTrafficSpikesAsync(perClient, windowStartUtc, windowEndUtc, cancellationToken));
        }

        alerts.AddRange(await DetectBlockedDomainContactsAsync(windowStartUtc, windowEndUtc, cancellationToken));

        if (_options.DetectSuspiciousDomains)
        {
            alerts.AddRange(await DetectSuspiciousDomainsAsync(windowStartUtc, windowEndUtc, cancellationToken));
        }

        var newAlerts = await FilterDuplicatesAsync(alerts, cancellationToken);
        if (newAlerts.Count > 0)
        {
            _unitOfWork.Alerts.AddRange(newAlerts);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Raised {Count} suspicious-activity alerts", newAlerts.Count);
            await NotifyAsync(newAlerts, cancellationToken);
        }

        return newAlerts.Count;
    }

    private async Task<List<SuspiciousActivityAlert>> DetectTrafficSpikesAsync(
        IReadOnlyList<Domain.Statistics.ClientWindowStat> perClient,
        DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken)
    {
        var baselineWindows = Math.Max(1, _options.SpikeBaselineWindows);
        var baselineStart = windowStartUtc.AddMinutes(-_options.WindowMinutes * (double)baselineWindows);

        var baseline = await _unitOfWork.ProxyLogs
            .GetClientWindowStatsAsync(baselineStart, windowStartUtc, cancellationToken);
        var baselineByClient = baseline.ToDictionary(c => c.ClientIp, c => c.Requests);

        var result = new List<SuspiciousActivityAlert>();
        foreach (var client in perClient)
        {
            if (client.Requests < _options.SpikeMinRequests)
            {
                continue;
            }

            if (!baselineByClient.TryGetValue(client.ClientIp, out var baseTotal) || baseTotal <= 0)
            {
                continue;
            }

            var avgPerWindow = baseTotal / (double)baselineWindows;
            if (avgPerWindow <= 0)
            {
                continue;
            }

            if (client.Requests >= avgPerWindow * _options.SpikeMultiplier)
            {
                result.Add(CreateAlert(client.ClientIp, AlertType.TrafficSpike, windowStartUtc, windowEndUtc,
                    $"{client.Requests} requests this window vs baseline avg {avgPerWindow:F0} (×{client.Requests / avgPerWindow:F1})"));
            }
        }

        return result;
    }

    private async Task<List<SuspiciousActivityAlert>> DetectBlockedDomainContactsAsync(
        DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken)
    {
        var contacts = await _unitOfWork.ProxyLogs
            .GetBlockedDomainContactsAsync(windowStartUtc, windowEndUtc, cancellationToken);

        return contacts
            .Select(c => CreateAlert(c.ClientIp, AlertType.BlockedDomainContact, windowStartUtc, windowEndUtc,
                $"Contacted blocklisted domain {c.Domain} ({c.Count}x)"))
            .ToList();
    }

    private async Task<List<SuspiciousActivityAlert>> DetectSuspiciousDomainsAsync(
        DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken)
    {
        var contacts = await _unitOfWork.ProxyLogs
            .GetClientHostContactsAsync(windowStartUtc, windowEndUtc, cancellationToken);

        var allowlist = (await _unitOfWork.AllowedDomains.GetAllDomainNamesAsync(cancellationToken)).ToHashSet();

        var result = new List<SuspiciousActivityAlert>();
        var autoBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var client in contacts)
        {
            foreach (var hit in client.Hosts)
            {
                if (!DomainEntropy.LooksAlgorithmic(hit.Host, _options.DomainEntropyThreshold, _options.DomainMinLabelLength))
                {
                    continue;
                }

                if (IsAllowlisted(hit.Host, allowlist))
                {
                    continue;
                }

                result.Add(CreateAlert(client.ClientIp, AlertType.SuspiciousDomain, windowStartUtc, windowEndUtc,
                    $"Contacted algorithmically-generated domain {hit.Host} ({hit.Count}x)"));

                if (_options.AutoBlockSuspiciousHosts && autoBlocked.Add(hit.Host))
                {
                    await AutoBlockAsync(hit.Host, cancellationToken);
                }
            }
        }

        return result;
    }

    private async Task AutoBlockAsync(string host, CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.BlockedDomains.FindByDomainAsync(host, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        _unitOfWork.BlockedDomains.Add(new BlockedDomain
        {
            Domain = host,
            Source = BlockSource.Auto,
            Reason = "Auto-blocked: algorithmically-generated domain",
            CreatedAtUtc = now,
            IsActive = true,
            ExpiresAtUtc = _options.AutoBlockTtlHours > 0 ? now.AddHours(_options.AutoBlockTtlHours) : null
        });

        _logger.LogWarning("Auto-blocked suspicious domain {Host}", host);
    }

    private static bool IsAllowlisted(string host, HashSet<string> allowlist)
    {
        if (allowlist.Count == 0)
        {
            return false;
        }

        var current = host;
        while (!string.IsNullOrEmpty(current))
        {
            if (allowlist.Contains(current))
            {
                return true;
            }

            var dot = current.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
            {
                break;
            }

            current = current[(dot + 1)..];
        }

        return false;
    }

    private async Task NotifyAsync(List<SuspiciousActivityAlert> alerts, CancellationToken cancellationToken)
    {
        if (!_notifications.HasEnabledChannels)
        {
            return;
        }

        var severity = alerts.Any(a => a.Type is AlertType.BlockedDomainContact or AlertType.SuspiciousDomain)
            ? NotificationSeverity.Critical
            : NotificationSeverity.Warning;

        var lines = alerts
            .Take(20)
            .Select(a => $"• {a.ClientIp} — {a.Type}: {a.Description}");
        var more = alerts.Count > 20 ? $"\n… and {alerts.Count - 20} more" : string.Empty;

        var message = new NotificationMessage(
            $"{alerts.Count} new alert(s)",
            string.Join('\n', lines) + more,
            severity);

        await _notifications.DispatchAsync(message, cancellationToken);
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
        var recent = await _unitOfWork.Alerts.GetRecentOpenAlertKeysAsync(since, cancellationToken);

        var recentKeys = recent.ToHashSet();
        var seen = new HashSet<AlertKey>();
        var result = new List<SuspiciousActivityAlert>();
        foreach (var candidate in candidates)
        {
            var key = new AlertKey(candidate.ClientIp, candidate.Type);
            if (recentKeys.Contains(key) || !seen.Add(key))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
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
