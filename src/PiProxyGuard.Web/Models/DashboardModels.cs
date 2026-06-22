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

/// <summary>Traffic grouped by client country for the traffic page (GeoIP).</summary>
public sealed record CountryTraffic(
    string Code,
    string Name,
    long Requests,
    long Bytes,
    long DeniedRequests,
    int ClientCount);

/// <summary>A top client device enriched with its resolved country (GeoIP).</summary>
public sealed record ClientTrafficView(
    string ClientIp,
    long Requests,
    long Bytes,
    long DeniedRequests,
    string? CountryCode,
    string? CountryName);

/// <summary>
/// Security / integration settings as shown on the Settings page. The API keys
/// are never sent back to the browser — only a "has value" flag and a short
/// masked preview, exactly like the notification secrets.
/// </summary>
public sealed record SecuritySettingsViewModel(
    string? GeoIpCountryDatabasePath,
    string? GeoIpAsnDatabasePath,
    bool GeoIpActive,
    bool UrlhausEnabled,
    bool VirusTotalHasKey,
    string? VirusTotalKeyPreview,
    bool AbuseIpDbHasKey,
    string? AbuseIpDbKeyPreview,
    int AbuseIpDbScoreThreshold,
    bool DailyDigestEnabled,
    int DailyReportHour,
    int DigestWindowHours,
    bool ScanEnabled,
    int ScanIntervalHours,
    int ScanLookbackHours,
    int ScanTopDomains,
    int ScanRequestDelayMs,
    bool ScanAutoBlock,
    int ScanAutoBlockTtlHours);

/// <summary>
/// The editable security settings posted from the Settings page. A null or empty
/// API key (<see cref="VirusTotalApiKey"/> / <see cref="AbuseIpDbApiKey"/>) means
/// "keep the stored value", so the user never has to re-type it.
/// </summary>
public sealed record SecuritySettingsInput
{
    public string? GeoIpCountryDatabasePath { get; init; }
    public string? GeoIpAsnDatabasePath { get; init; }

    public bool UrlhausEnabled { get; init; } = true;
    public string? VirusTotalApiKey { get; init; }
    public string? AbuseIpDbApiKey { get; init; }
    public int AbuseIpDbScoreThreshold { get; init; } = 50;

    public bool DailyDigestEnabled { get; init; }
    public int DailyReportHour { get; init; } = 8;
    public int DigestWindowHours { get; init; } = 24;

    public bool ScanEnabled { get; init; }
    public int ScanIntervalHours { get; init; } = 6;
    public int ScanLookbackHours { get; init; } = 24;
    public int ScanTopDomains { get; init; } = 50;
    public int ScanRequestDelayMs { get; init; } = 1500;
    public bool ScanAutoBlock { get; init; } = true;
    public int ScanAutoBlockTtlHours { get; init; }
}
