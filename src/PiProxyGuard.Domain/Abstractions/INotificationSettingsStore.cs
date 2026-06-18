namespace PiProxyGuard.Domain.Abstractions;

/// <summary>
/// An immutable view of the current notification configuration. Channels and
/// the dispatcher read this instead of <c>IOptions</c> so settings edited in
/// the dashboard take effect at runtime, across processes.
/// </summary>
public sealed record NotificationSettingsSnapshot(
    NotificationSeverity MinimumSeverity,
    bool TelegramEnabled,
    string? TelegramBotToken,
    string? TelegramChatId,
    bool EmailEnabled,
    string? EmailHost,
    int EmailPort,
    bool EmailUseSsl,
    string? EmailUsername,
    string? EmailPassword,
    string? EmailFrom,
    string? EmailTo)
{
    /// <summary>True only when Telegram is enabled and has a token and chat id.</summary>
    public bool TelegramConfigured =>
        TelegramEnabled
        && !string.IsNullOrWhiteSpace(TelegramBotToken)
        && !string.IsNullOrWhiteSpace(TelegramChatId);

    /// <summary>True only when e-mail is enabled and has a host, sender and recipient.</summary>
    public bool EmailConfigured =>
        EmailEnabled
        && !string.IsNullOrWhiteSpace(EmailHost)
        && !string.IsNullOrWhiteSpace(EmailFrom)
        && !string.IsNullOrWhiteSpace(EmailTo);
}

/// <summary>
/// Reads and writes the runtime notification settings. Implementations cache
/// the database row and refresh it periodically, so the synchronous
/// <see cref="Current"/> is cheap and the asynchronous <see cref="GetAsync"/>
/// is always up to date within the cache window.
/// </summary>
public interface INotificationSettingsStore
{
    /// <summary>Last known settings (cache); never blocks, never null.</summary>
    NotificationSettingsSnapshot Current { get; }

    /// <summary>Returns the current settings, refreshing the cache if it is stale.</summary>
    Task<NotificationSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists new settings and updates the cache immediately.</summary>
    Task SaveAsync(NotificationSettingsSnapshot snapshot, CancellationToken cancellationToken = default);
}
