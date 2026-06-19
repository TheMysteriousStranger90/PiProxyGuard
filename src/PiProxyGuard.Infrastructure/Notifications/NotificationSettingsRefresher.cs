using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.Notifications;

/// <summary>
/// Periodically refreshes the cached notification settings so the synchronous
/// <see cref="INotificationSettingsStore.Current"/> stays fresh in every process
/// (including the Worker, which never edits the settings itself). Runs in both
/// the Worker and the API host.
/// </summary>
public sealed class NotificationSettingsRefresher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly INotificationSettingsStore _store;
    private readonly ILogger<NotificationSettingsRefresher> _logger;

    public NotificationSettingsRefresher(
        INotificationSettingsStore store,
        ILogger<NotificationSettingsRefresher> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm the cache once at startup, then keep it fresh.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.GetAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Notification settings refresh failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
