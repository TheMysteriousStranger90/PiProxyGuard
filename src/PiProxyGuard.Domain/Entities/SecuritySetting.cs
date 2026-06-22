using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Domain.Entities;

/// <summary>
/// Runtime security/integration configuration, editable from the dashboard
/// Settings page. Stored as a single row (<see cref="Id"/> is always <c>1</c>)
/// so the Worker and the API — which run as separate processes sharing the
/// SQLite file — read the same settings. When the row is absent the values fall
/// back to the <c>GeoIp</c>, <c>ThreatIntel</c>, <c>Reports</c> and
/// <c>ThreatIntelScan</c> sections of <c>appsettings</c>/environment, so existing
/// config-driven installs keep working without touching the UI.
/// </summary>
public class SecuritySetting
{
    /// <summary>Primary key. Always <c>1</c> — this table holds a single row.</summary>
    public long Id { get; set; }

    // ---- GeoIP (MaxMind GeoLite2) ----

    /// <summary>Path to a MaxMind GeoLite2-Country .mmdb file (empty = disabled).</summary>
    public string? GeoIpCountryDatabasePath { get; set; }

    /// <summary>Path to a MaxMind GeoLite2-ASN .mmdb file (empty = disabled).</summary>
    public string? GeoIpAsnDatabasePath { get; set; }

    // ---- Threat intelligence ----

    /// <summary>Whether the free, key-less URLhaus host lookup is enabled.</summary>
    public bool UrlhausEnabled { get; set; } = true;

    /// <summary>VirusTotal v3 API key. When set, the VirusTotal domain lookup is enabled.</summary>
    public string? VirusTotalApiKey { get; set; }

    /// <summary>AbuseIPDB API key. When set, the AbuseIPDB IP lookup is enabled.</summary>
    public string? AbuseIpDbApiKey { get; set; }

    /// <summary>AbuseIPDB abuse-confidence score (1-100) at or above which a host is malicious.</summary>
    public int AbuseIpDbScoreThreshold { get; set; } = 50;

    // ---- Scheduled daily digest ----

    /// <summary>Master switch for the Worker's scheduled daily digest.</summary>
    public bool DailyDigestEnabled { get; set; }

    /// <summary>Local-time hour of day (0-23) the digest is sent.</summary>
    public int DailyReportHour { get; set; } = 8;

    /// <summary>How many hours of traffic the digest covers.</summary>
    public int DigestWindowHours { get; set; } = 24;

    /// <summary>Subject line / title of the digest.</summary>
    public string DigestTitle { get; set; } = "PiProxyGuard daily digest";

    /// <summary>Severity the digest is delivered at (must pass the notification threshold).</summary>
    public NotificationSeverity DigestSeverity { get; set; } = NotificationSeverity.Warning;

    // ---- Background threat-intel scan ----

    /// <summary>Master switch for the Worker's background threat-intel scan.</summary>
    public bool ScanEnabled { get; set; }

    /// <summary>How often the scan runs (hours).</summary>
    public int ScanIntervalHours { get; set; } = 6;

    /// <summary>How far back to look for the busiest hosts to scan (hours).</summary>
    public int ScanLookbackHours { get; set; } = 24;

    /// <summary>Number of top hosts (by request count) scanned each cycle.</summary>
    public int ScanTopDomains { get; set; } = 50;

    /// <summary>Delay between individual provider lookups (ms), to respect rate limits.</summary>
    public int ScanRequestDelayMs { get; set; } = 1500;

    /// <summary>When true, malicious hosts found by the scan are auto-blocked.</summary>
    public bool ScanAutoBlock { get; set; } = true;

    /// <summary>Lifetime of an auto-block created by the scan (hours). 0 = permanent.</summary>
    public int ScanAutoBlockTtlHours { get; set; }

    /// <summary>When the settings were last saved.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
