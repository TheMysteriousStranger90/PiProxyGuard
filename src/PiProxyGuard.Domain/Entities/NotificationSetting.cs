using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Domain.Entities;

/// <summary>
/// Runtime notification configuration, editable from the dashboard. Stored as a
/// single row (<see cref="Id"/> is always <c>1</c>) so the Worker and the API —
/// which run as separate processes sharing the SQLite file — read the same
/// settings. When the row is absent the values fall back to the
/// <c>Notifications</c> section of <c>appsettings</c>/environment, so existing
/// env-configured installs keep working without touching the UI.
/// </summary>
public class NotificationSetting
{
    /// <summary>Primary key. Always <c>1</c> — this table holds a single row.</summary>
    public long Id { get; set; }

    /// <summary>Lowest severity that is actually delivered.</summary>
    public NotificationSeverity MinimumSeverity { get; set; } = NotificationSeverity.Warning;

    // ---- Telegram ----

    /// <summary>Whether the Telegram channel is turned on.</summary>
    public bool TelegramEnabled { get; set; }

    /// <summary>Telegram Bot API token (from \@BotFather).</summary>
    public string? TelegramBotToken { get; set; }

    /// <summary>Target chat id the bot posts to (user, group or channel).</summary>
    public string? TelegramChatId { get; set; }

    // ---- Email (SMTP) ----

    /// <summary>Whether the e-mail channel is turned on.</summary>
    public bool EmailEnabled { get; set; }

    /// <summary>SMTP server host name.</summary>
    public string? EmailHost { get; set; }

    /// <summary>SMTP server port (typically 587 for STARTTLS, 465 for SSL).</summary>
    public int EmailPort { get; set; } = 587;

    /// <summary>Whether to connect over SSL/TLS.</summary>
    public bool EmailUseSsl { get; set; } = true;

    /// <summary>SMTP username (optional for anonymous relays).</summary>
    public string? EmailUsername { get; set; }

    /// <summary>SMTP password (optional for anonymous relays).</summary>
    public string? EmailPassword { get; set; }

    /// <summary>From address.</summary>
    public string? EmailFrom { get; set; }

    /// <summary>Comma-separated list of recipient addresses.</summary>
    public string? EmailTo { get; set; }

    /// <summary>When the settings were last saved.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
