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
}

public class DetectionOptions
{
    public const string SectionName = "Detection";

    /// <summary>How often the detector runs.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Length of the sliding analysis window.</summary>
    public int WindowMinutes { get; set; } = 5;

    /// <summary>Requests per client per window before a HighRequestRate alert fires.</summary>
    public int MaxRequestsPerWindow { get; set; } = 600;

    /// <summary>Bytes per client per window before a HighTrafficVolume alert fires (default 500 MB).</summary>
    public long MaxBytesPerWindow { get; set; } = 500L * 1024 * 1024;

    /// <summary>Denied requests per client per window before a RepeatedDeniedRequests alert fires.</summary>
    public int MaxDeniedPerWindow { get; set; } = 20;

    /// <summary>When true, hosts contacted via denied requests are added to the blocklist automatically.</summary>
    public bool AutoBlockSuspiciousHosts { get; set; }
}

public class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Optional API key. When set, requests must send it in the X-Api-Key header.</summary>
    public string? ApiKey { get; set; }
}
