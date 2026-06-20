namespace PiProxyGuard.Web.Models;

// View models the dashboard renders. They intentionally live in the Web library
// (not the API's wire contracts) so the UI is decoupled from the REST surface —
// the two can evolve independently. DashboardService maps domain entities into
// these; the API controllers keep their own DTOs in PiProxyGuard.Api.Contracts.

/// <summary>A blocked domain as shown in the blocklist page.</summary>
public sealed record BlockedDomainDto(
    long Id,
    string Domain,
    string Source,
    string? Reason,
    DateTime CreatedAtUtc,
    bool IsActive,
    DateTime? ExpiresAtUtc);

/// <summary>An allowlisted domain as shown in the allowlist page.</summary>
public sealed record AllowedDomainDto(long Id, string Domain, string? Reason, DateTime CreatedAtUtc);

public sealed record TunneledDomainDto(long Id, string Domain, string? Reason, DateTime CreatedAtUtc);

/// <summary>A suspicious-activity alert as shown in the alerts page and live toasts.</summary>
public sealed record AlertDto(
    long Id,
    string ClientIp,
    string Type,
    string Description,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    DateTime DetectedAtUtc,
    bool IsAcknowledged);

/// <summary>Traffic grouped by category for the traffic page.</summary>
public sealed record CategoryTrafficDto(string Category, long Requests, long Bytes);

/// <summary>
/// Notification settings as shown on the Settings page. Secrets (the bot token
/// and SMTP password) are never sent back to the browser — only a "has value"
/// flag and a short masked preview, so the page can show that a secret is saved
/// without exposing it.
/// </summary>
public sealed record NotificationSettingsViewModel(
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
/// The editable notification settings posted from the Settings page. A null or
/// empty secret (<see cref="TelegramBotToken"/> / <see cref="EmailPassword"/>)
/// means "keep the stored value", so the user never has to re-type it.
/// </summary>
public sealed record NotificationSettingsInput
{
    public string MinimumSeverity { get; init; } = "Warning";

    public bool TelegramEnabled { get; init; }
    public string? TelegramBotToken { get; init; }
    public string? TelegramChatId { get; init; }

    public bool EmailEnabled { get; init; }
    public string? EmailHost { get; init; }
    public int EmailPort { get; init; } = 587;
    public bool EmailUseSsl { get; init; } = true;
    public string? EmailUsername { get; init; }
    public string? EmailPassword { get; init; }
    public string? EmailFrom { get; init; }
    public string? EmailTo { get; init; }
}
