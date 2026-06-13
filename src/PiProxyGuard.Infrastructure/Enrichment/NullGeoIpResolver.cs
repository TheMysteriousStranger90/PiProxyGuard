using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.Enrichment;

/// <summary>
/// Default <see cref="IGeoIpResolver"/> used when no MaxMind GeoLite2 database
/// is configured. Always reports disabled and returns empty info, so the rest
/// of the system works identically with or without GeoIP data. Drop in a real
/// MaxMind-backed resolver to light up country/ASN enrichment.
/// </summary>
public sealed class NullGeoIpResolver : IGeoIpResolver
{
    public bool IsEnabled => false;

    public GeoIpInfo Resolve(string ipAddress) => GeoIpInfo.Empty;
}
