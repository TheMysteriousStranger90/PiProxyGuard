namespace PiProxyGuard.Api.Contracts;

// ---------------------------------------------------------------------------
// Notifications
// ---------------------------------------------------------------------------

/// <summary>
/// Current notification settings. Secrets (the Telegram bot token and the SMTP
/// password) are never returned — only a "has value" flag and, for the token, a
/// short masked preview.
/// </summary>
public record NotificationSettingsDto(
    string MinimumSeverity,
    bool TelegramEnabled,
    bool TelegramHasToken,
    string? TelegramTokenPreview,
    string? TelegramChatId,
    bool EmailEnabled,
    string? EmailHost,
    int EmailPort,
    bool EmailUseSsl,
    string? EmailUsername,
    bool EmailHasPassword,
    string? EmailFrom,
    string? EmailTo);

/// <summary>
/// New notification settings. A null/empty secret (<see cref="TelegramBotToken"/>
/// or <see cref="EmailPassword"/>) keeps the value already stored, so clients
/// never have to resend it.
/// </summary>
public record UpdateNotificationSettingsRequest(
    string MinimumSeverity,
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
    string? EmailTo);

/// <summary>Asks the server to send a test message through one channel ("Telegram" or "Email").</summary>
public record SendTestNotificationRequest(string Channel);
