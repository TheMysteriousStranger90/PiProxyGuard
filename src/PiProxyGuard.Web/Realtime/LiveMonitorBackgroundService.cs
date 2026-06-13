using Microsoft.AspNetCore.SignalR;
using PiProxyGuard.Web.Services;

namespace PiProxyGuard.Web.Realtime;

/// <summary>
/// Single heartbeat that drives all live UI updates. On each tick it:
/// <list type="bullet">
///   <item>looks for alerts the Worker has written since the last tick and
///   pushes each new one to subscribers (in-app components + SignalR clients);</item>
///   <item>raises a <c>DataChanged</c> heartbeat so open dashboards re-pull
///   their stats.</item>
/// </list>
/// Because alerts are produced by a separate Worker process, polling the shared
/// database is the reliable cross-process signal; the interval is small and the
/// query is a single indexed lookup.
/// </summary>
public sealed class LiveMonitorBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly DashboardService _dashboard;
    private readonly LiveUpdateNotifier _notifier;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<LiveMonitorBackgroundService> _logger;

    private long _lastSeenAlertId;

    public LiveMonitorBackgroundService(
        DashboardService dashboard,
        LiveUpdateNotifier notifier,
        IHubContext<DashboardHub> hub,
        ILogger<LiveMonitorBackgroundService> logger)
    {
        _dashboard = dashboard;
        _notifier = notifier;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start from the current tail so we only announce genuinely new alerts.
        try
        {
            _lastSeenAlertId = await _dashboard.GetLatestAlertIdAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Live monitor could not read the initial alert id; starting from 0.");
        }

        using var timer = new PeriodicTimer(Interval);
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await PublishNewAlertsAsync(stoppingToken).ConfigureAwait(false);
                _notifier.RaiseDataChanged();
                await _hub.Clients.All.SendAsync("dataChanged", stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Live monitor heartbeat failed; will retry next tick.");
            }
        }
    }

    private async Task PublishNewAlertsAsync(CancellationToken ct)
    {
        var recent = await _dashboard.GetAlertsAsync(onlyUnacknowledged: false, count: 50, ct).ConfigureAwait(false);
        var fresh = recent.Where(a => a.Id > _lastSeenAlertId).OrderBy(a => a.Id).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        foreach (var alert in fresh)
        {
            _notifier.RaiseAlert(alert);
            await _hub.Clients.All.SendAsync("alertRaised", alert, ct).ConfigureAwait(false);
        }

        _lastSeenAlertId = fresh[^1].Id;
        _logger.LogInformation("Live monitor announced {Count} new alert(s).", fresh.Count);
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
