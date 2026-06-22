namespace PiProxyGuard.Domain.Abstractions;

/// <summary>
/// An immutable view of the current security/integration configuration (GeoIP,
/// threat-intel providers, the scheduled digest and the background scan). The
/// GeoIP resolver, threat-intel clients and the Worker's background services
/// read this instead of <c>IOptions</c> so settings edited in the dashboard
/// take effect at runtime, across the separate Worker and API processes.
/// </summary>
public sealed record SecuritySettingsSnapshot(
    // GeoIP
    string? GeoIpCountryDatabasePath,
    string? GeoIpAsnDatabasePath,
    // Threat intelligence
    bool UrlhausEnabled,
    string? VirusTotalApiKey,
    string? AbuseIpDbApiKey,
    int AbuseIpDbScoreThreshold,
    // Scheduled daily digest
    bool DailyDigestEnabled,
    int DailyReportHour,
    int DigestWindowHours,
    string DigestTitle,
    NotificationSeverity DigestSeverity,
    // Background threat-intel scan
    bool ScanEnabled,
    int ScanIntervalHours,
    int ScanLookbackHours,
    int ScanTopDomains,
    int ScanRequestDelayMs,
    bool ScanAutoBlock,
    int ScanAutoBlockTtlHours)
{
    /// <summary>True when a VirusTotal API key is configured.</summary>
    public bool VirusTotalConfigured => !string.IsNullOrWhiteSpace(VirusTotalApiKey);

    /// <summary>True when an AbuseIPDB API key is configured.</summary>
    public bool AbuseIpDbConfigured => !string.IsNullOrWhiteSpace(AbuseIpDbApiKey);

    /// <summary>True when at least one GeoIP database path is configured.</summary>
    public bool GeoIpConfigured =>
        !string.IsNullOrWhiteSpace(GeoIpCountryDatabasePath)
        || !string.IsNullOrWhiteSpace(GeoIpAsnDatabasePath);
}

/// <summary>
/// Reads and writes the runtime security settings. Implementations cache the
/// database row and refresh it periodically, so the synchronous
/// <see cref="Current"/> is cheap and the asynchronous <see cref="GetAsync"/>
/// is always up to date within the cache window.
/// </summary>
public interface ISecuritySettingsStore
{
    /// <summary>Last known settings (cache); never blocks, never null.</summary>
    SecuritySettingsSnapshot Current { get; }

    /// <summary>Returns the current settings, refreshing the cache if it is stale.</summary>
    Task<SecuritySettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists new settings and updates the cache immediately.</summary>
    Task SaveAsync(SecuritySettingsSnapshot snapshot, CancellationToken cancellationToken = default);
}
