namespace PiProxyGuard.Domain.Abstractions;

/// <summary>Severity hint passed to notification channels.</summary>
public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

/// <summary>A channel-agnostic notification ready to be delivered.</summary>
/// <param name="Title">Short subject line.</param>
/// <param name="Body">Plain-text body (channels may format it).</param>
/// <param name="Severity">How important the message is.</param>
public sealed record NotificationMessage(
    string Title,
    string Body,
    NotificationSeverity Severity = NotificationSeverity.Warning);

/// <summary>
/// Delivers a notification to a single channel (Telegram, e-mail, ...).
/// Implementations must be safe to call when disabled — they simply report
/// <see cref="IsEnabled"/> = false and do nothing — so the caller never has
/// to know which channels are configured.
/// </summary>
public interface INotificationSender
{
    /// <summary>Channel name, for logging (e.g. "Telegram").</summary>
    string Channel { get; }

    /// <summary>True when the channel has enough configuration to send.</summary>
    bool IsEnabled { get; }

    /// <summary>Sends the message. Returns false on failure (never throws).</summary>
    Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fans a single notification out to every configured channel. Registered
/// even when no channel is enabled, in which case it is a no-op.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>True when at least one underlying channel is enabled.</summary>
    bool HasEnabledChannels { get; }

    /// <summary>Delivers the message to all enabled channels. Returns how many succeeded.</summary>
    Task<int> DispatchAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
