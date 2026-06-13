namespace PiProxyGuard.Domain.Abstractions;

/// <summary>Geographic / network owner information for an IP address.</summary>
/// <param name="CountryIsoCode">Two-letter ISO country code, or null.</param>
/// <param name="CountryName">Human-readable country name, or null.</param>
/// <param name="AutonomousSystemNumber">ASN, or null.</param>
/// <param name="AutonomousSystemOrganization">AS organisation name, or null.</param>
public sealed record GeoIpInfo(
    string? CountryIsoCode,
    string? CountryName,
    long? AutonomousSystemNumber,
    string? AutonomousSystemOrganization)
{
    public static GeoIpInfo Empty { get; } = new(null, null, null, null);
}

/// <summary>
/// Resolves an IP address to country/ASN. The default implementation is a
/// no-op; plug in an offline MaxMind GeoLite2 database to enable it. Always
/// resolve through this abstraction so callers degrade gracefully when no
/// database is configured.
/// </summary>
public interface IGeoIpResolver
{
    /// <summary>True when a backing database is loaded.</summary>
    bool IsEnabled { get; }

    /// <summary>Resolves an IP. Returns <see cref="GeoIpInfo.Empty"/> when unknown.</summary>
    GeoIpInfo Resolve(string ipAddress);
}
