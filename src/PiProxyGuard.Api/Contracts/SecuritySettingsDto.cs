namespace PiProxyGuard.Api.Contracts;

/// <summary>
/// Security / integration settings returned by the REST API. The API keys are
/// never sent back — only a "configured" flag and a short masked preview, the
/// same way the notification secrets are handled.
/// </summary>
public record SecuritySettingsDto(
    string? GeoIpCountryDatabasePath,
    string? GeoIpAsnDatabasePath,
    bool UrlhausEnabled,
    bool VirusTotalConfigured,
    string? VirusTotalKeyPreview,
    bool AbuseIpDbConfigured,
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
/// The editable security settings. A null or empty API key
/// (<see cref="VirusTotalApiKey"/> / <see cref="AbuseIpDbApiKey"/>) means
/// "keep the stored value".
/// </summary>
public class UpdateSecuritySettingsRequest
{
    public string? GeoIpCountryDatabasePath { get; set; }
    public string? GeoIpAsnDatabasePath { get; set; }

    public bool UrlhausEnabled { get; set; } = true;
    public string? VirusTotalApiKey { get; set; }
    public string? AbuseIpDbApiKey { get; set; }
    public int AbuseIpDbScoreThreshold { get; set; } = 50;

    public bool DailyDigestEnabled { get; set; }
    public int DailyReportHour { get; set; } = 8;
    public int DigestWindowHours { get; set; } = 24;

    public bool ScanEnabled { get; set; }
    public int ScanIntervalHours { get; set; } = 6;
    public int ScanLookbackHours { get; set; } = 24;
    public int ScanTopDomains { get; set; } = 50;
    public int ScanRequestDelayMs { get; set; } = 1500;
    public bool ScanAutoBlock { get; set; } = true;
    public int ScanAutoBlockTtlHours { get; set; }
}
