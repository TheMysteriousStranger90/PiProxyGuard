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
