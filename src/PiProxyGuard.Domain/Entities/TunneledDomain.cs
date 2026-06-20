namespace PiProxyGuard.Domain.Entities;

/// <summary>
/// A domain whose traffic must be routed through the configured upstream
/// proxy (the "upstream tunnel") instead of going out directly. PiProxyGuard
/// writes these entries to a Squid dstdomain ACL (tunnel_domains.acl) that the
/// <c>cache_peer_access</c> / <c>never_direct</c> rules in squid.conf use to
/// forward only the listed domains via the parent proxy (e.g. a VPN-side proxy
/// or Tor). Matching is suffix-based, so an entry for "example.com" also
/// tunnels "cdn.example.com".
/// </summary>
public class TunneledDomain
{
    public long Id { get; set; }

    /// <summary>Domain name, lower-case, without scheme (e.g. "example.com").</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Human-readable reason for routing this domain through the tunnel.</summary>
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
