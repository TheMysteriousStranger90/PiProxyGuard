namespace PiProxyGuard.Domain.Common;

public static class DomainUtils
{
    /// <summary>
    /// Extracts the host from a URL as logged by Squid. Handles regular URLs
    /// ("https://example.com/path"), CONNECT targets ("example.com:443")
    /// and bare hosts. Returns an empty string when nothing sensible is found.
    /// </summary>
    public static string ExtractHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var value = url.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host.ToLowerInvariant();
        }

        // CONNECT requests are logged as "host:port".
        var colonIndex = value.IndexOf(':');
        if (colonIndex > 0 && int.TryParse(value[(colonIndex + 1)..], out _))
        {
            value = value[..colonIndex];
        }

        var slashIndex = value.IndexOf('/');
        if (slashIndex > 0)
        {
            value = value[..slashIndex];
        }

        return IsValidHost(value) ? value.ToLowerInvariant() : string.Empty;
    }

    /// <summary>
    /// Normalizes a domain from a blocklist feed: trims, lower-cases and
    /// validates. Returns null when the value is not a usable domain.
    /// </summary>
    public static string? NormalizeDomain(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var domain = raw.Trim().TrimEnd('.').ToLowerInvariant();

        // Strip a leading wildcard often used in blocklists ("*.example.com").
        if (domain.StartsWith("*.", StringComparison.Ordinal))
        {
            domain = domain[2..];
        }

        // Blocklists are dstdomain ACLs — only proper DNS names are usable,
        // raw IP addresses (e.g. "0.0.0.0" from hosts files) are rejected.
        if (domain.Length > 253 || !domain.Contains('.') ||
            Uri.CheckHostName(domain) != UriHostNameType.Dns)
        {
            return null;
        }

        // Skip localhost-style entries that hosts files contain.
        return domain is "localhost" or "localhost.localdomain" or "broadcasthost"
            ? null
            : domain;
    }

    private static bool IsValidHost(string value) =>
        value.Length is > 0 and <= 253 &&
        Uri.CheckHostName(value) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
}
