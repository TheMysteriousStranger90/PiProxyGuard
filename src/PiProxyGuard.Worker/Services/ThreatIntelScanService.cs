using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;

namespace PiProxyGuard.Worker.Services;

/// <summary>
/// Periodically takes the busiest recently-seen destination hosts, runs each
/// through the configured threat-intel providers and (optionally) auto-blocks
/// the ones that come back malicious — turning the on-demand
/// <c>/api/threat-intel/check</c> lookup into proactive, scheduled protection.
/// Already-blocked and allowlisted hosts are skipped, a small delay between
/// lookups respects provider rate limits, and findings are pushed to the
/// notification channels. Reads its configuration from the runtime
/// <see cref="ISecuritySettingsStore"/> each cycle, so it can be enabled/disabled
/// or retuned from the dashboard without a restart. Disabled by default.
/// </summary>
public class ThreatIntelScanService : BackgroundService
{
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThreatIntelClient _threatIntel;
    private readonly ISecuritySettingsStore _settings;
    private readonly ILogger<ThreatIntelScanService> _logger;

    public ThreatIntelScanService(
        IServiceScopeFactory scopeFactory,
        IThreatIntelClient threatIntel,
        ISecuritySettingsStore settings,
        ILogger<ThreatIntelScanService> logger)
    {
        _scopeFactory = scopeFactory;
        _threatIntel = threatIntel;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background threat-intel scan service started");

        // Let ingestion and the blocklist seed before the first scan.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var snapshot = _settings.Current;
            TimeSpan delay;

            if (!snapshot.ScanEnabled)
            {
                delay = DisabledPollInterval;
            }
            else
            {
                try
                {
                    await ScanAsync(snapshot, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Threat-intel scan cycle failed");
                }

                delay = TimeSpan.FromHours(Math.Clamp(snapshot.ScanIntervalHours, 1, 168));
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ScanAsync(SecuritySettingsSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!_threatIntel.IsEnabled)
        {
            _logger.LogDebug("Threat-intel scan skipped: no provider is enabled");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddHours(-Math.Clamp(snapshot.ScanLookbackHours, 1, 720));
        var topCount = Math.Clamp(snapshot.ScanTopDomains, 1, 1000);

        var hosts = await unitOfWork.ProxyLogs.GetTopHostsAsync(fromUtc, toUtc, topCount, cancellationToken);
        if (hosts.Count == 0)
        {
            return;
        }

        var alreadyBlocked = (await unitOfWork.BlockedDomains.GetActiveDomainNamesAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowlist = (await unitOfWork.AllowedDomains.GetAllDomainNamesAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var findings = new List<string>();
        var added = 0;
        var delay = Math.Clamp(snapshot.ScanRequestDelayMs, 0, 60000);

        foreach (var host in hosts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (alreadyBlocked.Contains(host.Host) || IsAllowlisted(host.Host, allowlist))
            {
                continue;
            }

            var verdict = await _threatIntel.CheckDomainAsync(host.Host, cancellationToken);
            if (verdict.IsMalicious)
            {
                findings.Add($"{host.Host} — {verdict.Source}: {verdict.Details}");

                if (snapshot.ScanAutoBlock
                    && await AutoBlockAsync(unitOfWork, host.Host, verdict, snapshot.ScanAutoBlockTtlHours, cancellationToken))
                {
                    alreadyBlocked.Add(host.Host);
                    added++;
                }
            }

            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        if (added > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Threat-intel scan auto-blocked {Count} malicious host(s)", added);
        }

        if (findings.Count > 0)
        {
            await NotifyAsync(scope, findings, added, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Threat-intel scan completed: no malicious hosts among top {Count}", hosts.Count);
        }
    }

    private static async Task<bool> AutoBlockAsync(
        IUnitOfWork unitOfWork, string host, ThreatVerdict verdict, int ttlHours, CancellationToken cancellationToken)
    {
        var existing = await unitOfWork.BlockedDomains.FindByDomainAsync(host, cancellationToken);
        if (existing is not null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        unitOfWork.BlockedDomains.Add(new BlockedDomain
        {
            Domain = host,
            Source = BlockSource.Auto,
            Reason = $"Threat-intel ({verdict.Source}): {verdict.Details}",
            CreatedAtUtc = now,
            IsActive = true,
            ExpiresAtUtc = ttlHours > 0 ? now.AddHours(ttlHours) : null
        });

        return true;
    }

    private static async Task NotifyAsync(
        IServiceScope scope, List<string> findings, int blocked, CancellationToken cancellationToken)
    {
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        if (!dispatcher.HasEnabledChannels)
        {
            return;
        }

        var lines = findings.Take(20).Select(f => $"• {f}");
        var more = findings.Count > 20 ? $"\n… and {findings.Count - 20} more" : string.Empty;
        var blockedNote = blocked > 0 ? $" ({blocked} auto-blocked)" : string.Empty;

        var message = new NotificationMessage(
            $"Threat-intel scan: {findings.Count} malicious host(s){blockedNote}",
            string.Join('\n', lines) + more,
            NotificationSeverity.Critical);

        await dispatcher.DispatchAsync(message, cancellationToken);
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
}
