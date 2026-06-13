namespace PiProxyGuard.Domain.Entities;

/// <summary>
/// A domain that must never be blocked, regardless of what the blocklist
/// feeds contain. The allowlist always wins over the blocklist: matching
/// entries are removed from the generated Squid ACL and excluded from
/// blocked-domain-contact alerts. Matching is suffix-based, so an entry for
/// "example.com" also protects "cdn.example.com".
/// </summary>
public class AllowedDomain
{
    public long Id { get; set; }

    /// <summary>Domain name, lower-case, without scheme (e.g. "example.com").</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Human-readable reason for the exception.</summary>
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
