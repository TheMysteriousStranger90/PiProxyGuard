namespace PiProxyGuard.Api.Contracts;

// ---------------------------------------------------------------------------
// Traffic statistics
// ---------------------------------------------------------------------------

public record TrafficSummaryDto(
    DateTime FromUtc,
    DateTime ToUtc,
    long TotalRequests,
    long TotalBytes,
    long DeniedRequests,
    int UniqueClients,
    int UniqueHosts);

public record TopHostDto(string Host, long Requests, long Bytes);

public record TopClientDto(string ClientIp, long Requests, long Bytes, long DeniedRequests);

public record TimelinePointDto(DateTime BucketStartUtc, long Requests, long Bytes);

public record StatusCodeDto(int StatusCode, long Count);

/// <summary>HTTP method breakdown — surfaces <c>ProxyLogEntry.Method</c>.</summary>
public record HttpMethodStatDto(string Method, long Requests, long Bytes);

/// <summary>Response content-type breakdown — surfaces <c>ProxyLogEntry.ContentType</c>.</summary>
public record ContentTypeStatDto(string ContentType, long Requests, long Bytes);

/// <summary>Squid result-code breakdown — surfaces <c>ProxyLogEntry.ResultCode</c>.</summary>
public record ResultCodeStatDto(string ResultCode, long Requests, long Bytes);

/// <summary>Per-host latency — surfaces <c>ProxyLogEntry.ElapsedMs</c>.</summary>
public record HostPerformanceDto(string Host, long Requests, double AverageElapsedMs, int MaxElapsedMs);

/// <summary>Operational view of the log ingestion bookmark (<c>LogIngestionState</c>).</summary>
public record IngestionStatusDto(
    string FilePath,
    long Offset,
    bool HasRotationFingerprint,
    DateTime UpdatedAtUtc);

// ---------------------------------------------------------------------------
// Blocklist
// ---------------------------------------------------------------------------

public record AddBlockedDomainRequest(string Domain, string? Reason, int? ExpiresInHours);

public record BlockedDomainDto(long Id, string Domain, string Source, string? Reason, DateTime CreatedAtUtc, bool IsActive, DateTime? ExpiresAtUtc);

// ---------------------------------------------------------------------------
// Alerts
// ---------------------------------------------------------------------------

public record AlertDto(
    long Id,
    string ClientIp,
    string Type,
    string Description,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    DateTime DetectedAtUtc,
    bool IsAcknowledged);

// ---------------------------------------------------------------------------
// Allowlist (1.2.0)
// ---------------------------------------------------------------------------

public record AddAllowedDomainRequest(string Domain, string? Reason);

public record AllowedDomainDto(long Id, string Domain, string? Reason, DateTime CreatedAtUtc);

public record AddTunneledDomainRequest(string Domain, string? Reason);

public record TunneledDomainDto(long Id, string Domain, string? Reason, DateTime CreatedAtUtc);

// ---------------------------------------------------------------------------
// Categories, threat-intel, diagnostics, digest, backup (1.2.0)
// ---------------------------------------------------------------------------

public record CategoryTrafficDto(string Category, long Requests, long Bytes);

public record ThreatVerdictDto(string Domain, bool IsMalicious, string Source, string? Details);

public record DiagnosticCheckDto(string Name, string Status, string Detail);

public record DiagnosticsReportDto(string Status, bool Healthy, DateTime GeneratedAtUtc, List<DiagnosticCheckDto> Checks);

public record DigestReportDto(string Title, DateTime FromUtc, DateTime ToUtc, DateTime GeneratedAtUtc, string Text);

public record BackupDto(
    string Version,
    DateTime ExportedAtUtc,
    List<BackupBlockedDomainDto> BlockedDomains,
    List<BackupAllowedDomainDto> AllowedDomains);

public record BackupBlockedDomainDto(string Domain, string Source, string? Reason, bool IsActive, DateTime? ExpiresAtUtc);

public record BackupAllowedDomainDto(string Domain, string? Reason);

public record RestoreResultDto(int BlockedImported, int AllowedImported, int Skipped);
