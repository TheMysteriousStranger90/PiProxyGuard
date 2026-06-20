namespace PiProxyGuard.Infrastructure.Options;

public class AccessLogOptions
{
    public const string SectionName = "AccessLog";

    /// <summary>Path to the Squid access log.</summary>
    public string Path { get; set; } = "/var/log/squid/access.log";

    /// <summary>How often the ingestion worker polls the log for new lines.</summary>
    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>Max number of parsed entries saved per batch.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Delete log entries older than this many days (0 = keep forever).</summary>
    public int RetentionDays { get; set; } = 90;
}

public class BlocklistFeedOptions
{
    /// <summary>Feed URL to download.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Payload format: "hosts", "plain" or "html".</summary>
    public string Format { get; set; } = "hosts";

    /// <summary>CSS selector for "html" feeds (AngleSharp), e.g. "table#domains td.domain".</summary>
    public string? CssSelector { get; set; }
}

public class BlocklistOptions
{
    public const string SectionName = "Blocklist";

    public List<BlocklistFeedOptions> Feeds { get; set; } = [];

    /// <summary>How often feeds are re-downloaded.</summary>
    public int UpdateIntervalHours { get; set; } = 12;

    /// <summary>Where the aggregated Squid ACL file is written.</summary>
    public string AclFilePath { get; set; } = "/etc/squid/blocked_domains.acl";

    /// <summary>Command executed after the ACL file changes (empty = skip).</summary>
    public string ReloadCommand { get; set; } = "squid -k reconfigure";

    /// <summary>
    /// Keep a ".bak" copy of the ACL before each rewrite and restore it if the
    /// reload command fails, so a bad blocklist can never wedge the proxy.
    /// </summary>
    public bool BackupAclBeforeWrite { get; set; } = true;
}

/// <summary>
/// Upstream-tunnel options: the Squid ACL that lists the domains routed through
/// the configured parent proxy (cache_peer) instead of going out directly. The
/// domain list itself is managed at runtime from the dashboard; the parent proxy
/// is wired up in squid.conf by deploy/upstream-tunnel/setup-upstream.sh.
/// </summary>
public class TunnelOptions
{
    public const string SectionName = "Tunnel";

    /// <summary>Where the upstream-tunnel Squid ACL file is written.</summary>
    public string AclFilePath { get; set; } = "/etc/squid/tunnel_domains.acl";

    /// <summary>Command executed after the ACL file changes (empty = skip).</summary>
    public string ReloadCommand { get; set; } = "squid -k reconfigure";
}

/// <summary>Per-client threshold overrides keyed by IP or CIDR prefix.</summary>
public class ClientProfileOptions
{
    /// <summary>Exact client IP or a CIDR like "192.168.10.0/24".</summary>
    public string Match { get; set; } = string.Empty;

    /// <summary>Optional friendly name for logs/alerts (e.g. "kids-tablet").</summary>
    public string? Name { get; set; }

    /// <summary>Multiplier applied to every default threshold (less than 1 = stricter).</summary>
    public double Strictness { get; set; } = 1.0;

    /// <summary>Override for requests-per-window (null = use default × strictness).</summary>
    public int? MaxRequestsPerWindow { get; set; }

    /// <summary>Override for bytes-per-window (null = use default × strictness).</summary>
    public long? MaxBytesPerWindow { get; set; }

    /// <summary>Override for denied-requests-per-window (null = use default × strictness).</summary>
    public int? MaxDeniedPerWindow { get; set; }
}

public class DetectionOptions
{
    public const string SectionName = "Detection";

    /// <summary>How often the detector runs.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Length of the sliding analysis window.</summary>
    public int WindowMinutes { get; set; } = 5;

    /// <summary>
    /// How long (minutes) the same client + alert-type is suppressed after an
    /// alert fires, to avoid spamming on repeated identical events. Lower it to
    /// get notifications more often; 0 disables time-based suppression entirely
    /// (duplicates within a single detection cycle are still collapsed).
    /// </summary>
    public int DuplicateSuppressionMinutes { get; set; } = 60;

    /// <summary>Requests per client per window before a HighRequestRate alert fires.</summary>
    public int MaxRequestsPerWindow { get; set; } = 600;

    /// <summary>Bytes per client per window before a HighTrafficVolume alert fires (default 500 MB).</summary>
    public long MaxBytesPerWindow { get; set; } = 500L * 1024 * 1024;

    /// <summary>Denied requests per client per window before a RepeatedDeniedRequests alert fires.</summary>
    public int MaxDeniedPerWindow { get; set; } = 20;

    /// <summary>When true, hosts contacted via denied requests are added to the blocklist automatically.</summary>
    public bool AutoBlockSuspiciousHosts { get; set; }

    /// <summary>
    /// Lifetime of an automatic block. After it elapses the auto-block sweep
    /// removes the entry. 0 = permanent auto-blocks.
    /// </summary>
    public int AutoBlockTtlHours { get; set; } = 24;

    /// <summary>Enable the traffic-spike detector (compares the window to a baseline).</summary>
    public bool DetectTrafficSpikes { get; set; } = true;

    /// <summary>
    /// How many multiples of the baseline window length to use as the baseline
    /// average. A spike fires when the current window exceeds the baseline rate
    /// by <see cref="SpikeMultiplier"/>×.
    /// </summary>
    public int SpikeBaselineWindows { get; set; } = 12;

    /// <summary>Factor over baseline that counts as a spike.</summary>
    public double SpikeMultiplier { get; set; } = 5.0;

    /// <summary>Minimum requests in the window before a spike can fire (ignore noise).</summary>
    public int SpikeMinRequests { get; set; } = 100;

    /// <summary>Enable the DGA / suspicious-domain detector.</summary>
    public bool DetectSuspiciousDomains { get; set; } = true;

    /// <summary>Shannon-entropy threshold (bits/char) above which a label looks machine-generated.</summary>
    public double DomainEntropyThreshold { get; set; } = 3.8;

    /// <summary>Minimum significant-label length considered for entropy scoring.</summary>
    public int DomainMinLabelLength { get; set; } = 12;

    /// <summary>Per-client threshold overrides (kids' devices, servers, ...).</summary>
    public List<ClientProfileOptions> ClientProfiles { get; set; } = [];
}

public class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Optional API key. When set, requests must send it in the X-Api-Key header.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// When greater than 0, applies a fixed-window rate limiter to /api/* of this
    /// many requests per client IP per minute; requests over the limit get 429.
    /// 0 (default) disables rate limiting.
    /// </summary>
    public int RateLimitPerMinute { get; set; }

    /// <summary>
    /// When true, enables HSTS and redirects HTTP to HTTPS. Requires an HTTPS
    /// Kestrel endpoint/certificate to be configured. Off by default.
    /// </summary>
    public bool UseHttpsRedirection { get; set; }
}

/// <summary>Telegram bot delivery settings.</summary>
public class TelegramNotificationOptions
{
    public string? BotToken { get; set; }

    public string? ChatId { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ChatId);
}

/// <summary>SMTP e-mail delivery settings.</summary>
public class EmailNotificationOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public bool Enabled =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(From) &&
        !string.IsNullOrWhiteSpace(To);
}

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Lowest severity that is actually delivered.</summary>
    public NotificationLevel MinimumSeverity { get; set; } = NotificationLevel.Warning;

    public TelegramNotificationOptions Telegram { get; set; } = new();

    public EmailNotificationOptions Email { get; set; } = new();
}

/// <summary>Mirror of <c>NotificationSeverity</c> for configuration binding.</summary>
public enum NotificationLevel
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public class GeoIpOptions
{
    public const string SectionName = "GeoIp";

    /// <summary>Path to a MaxMind GeoLite2-Country .mmdb file (empty = disabled).</summary>
    public string? CountryDatabasePath { get; set; }

    /// <summary>Path to a MaxMind GeoLite2-ASN .mmdb file (empty = disabled).</summary>
    public string? AsnDatabasePath { get; set; }
}

public class ThreatIntelOptions
{
    public const string SectionName = "ThreatIntel";

    /// <summary>Enable the free URLhaus host lookup.</summary>
    public bool UrlhausEnabled { get; set; } = true;

    /// <summary>Optional VirusTotal API key (reserved for a future provider).</summary>
    public string? VirusTotalApiKey { get; set; }

    /// <summary>Optional AbuseIPDB API key (reserved for a future provider).</summary>
    public string? AbuseIpDbApiKey { get; set; }
}
