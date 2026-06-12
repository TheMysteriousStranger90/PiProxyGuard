using PiProxyGuard.Domain.Enums;

namespace PiProxyGuard.Domain.Entities;

/// <summary>
/// A domain that should be blocked by the proxy. Aggregated from remote
/// blocklist feeds, manual entries and automatic detections, then written
/// out as a Squid ACL file.
/// </summary>
public class BlockedDomain
{
    public long Id { get; set; }

    /// <summary>Domain name, lower-case, without scheme (e.g. "ads.example.com").</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Where this entry came from.</summary>
    public BlockSource Source { get; set; }

    /// <summary>Feed URL or a human-readable reason.</summary>
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Manual/auto entries can be disabled without deleting them.</summary>
    public bool IsActive { get; set; } = true;
}
