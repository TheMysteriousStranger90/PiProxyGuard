using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.Notifications;

/// <summary>
/// Fans a notification out to every enabled <see cref="INotificationSender"/>.
/// Applies the configured minimum severity (read from the runtime
/// <see cref="INotificationSettingsStore"/>) and never throws, so callers (the
/// detector) can fire-and-forget.
/// </summary>
public class NotificationDispatcher : INotificationDispatcher
{
    private readonly IReadOnlyList<INotificationSender> _senders;
    private readonly INotificationSettingsStore _settings;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IEnumerable<INotificationSender> senders,
        INotificationSettingsStore settings,
        ILogger<NotificationDispatcher> logger)
    {
        _senders = senders.ToList();
        _settings = settings;
        _logger = logger;
    }

    public bool HasEnabledChannels => _senders.Any(s => s.IsEnabled);

    public async Task<int> DispatchAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Refresh the cache up front so channel enablement and the severity
        // threshold reflect the latest settings, even on the Worker process.
        var snapshot = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        if ((int)message.Severity < (int)snapshot.MinimumSeverity)
        {
            return 0;
        }

        var enabled = _senders.Where(s => s.IsEnabled).ToList();
        if (enabled.Count == 0)
        {
            return 0;
        }

        var delivered = 0;
        foreach (var sender in enabled)
        {
            try
            {
                if (await sender.SendAsync(message, cancellationToken).ConfigureAwait(false))
                {
                    delivered++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Notification channel {Channel} threw", sender.Channel);
            }
        }

        if (delivered > 0)
        {
            _logger.LogInformation("Notification '{Title}' delivered to {Count} channel(s)", message.Title, delivered);
        }

        return delivered;
    }
}
