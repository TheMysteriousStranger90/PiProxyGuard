using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Infrastructure.Notifications;

/// <summary>
/// Fans a notification out to every enabled <see cref="INotificationSender"/>.
/// Applies the configured minimum severity and never throws, so callers (the
/// detector) can fire-and-forget.
/// </summary>
public class NotificationDispatcher : INotificationDispatcher
{
    private readonly IReadOnlyList<INotificationSender> _senders;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IEnumerable<INotificationSender> senders,
        IOptions<NotificationOptions> options,
        ILogger<NotificationDispatcher> logger)
    {
        _senders = senders.ToList();
        _options = options.Value;
        _logger = logger;
    }

    public bool HasEnabledChannels => _senders.Any(s => s.IsEnabled);

    public async Task<int> DispatchAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if ((int)message.Severity < (int)_options.MinimumSeverity)
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
                if (await sender.SendAsync(message, cancellationToken))
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
