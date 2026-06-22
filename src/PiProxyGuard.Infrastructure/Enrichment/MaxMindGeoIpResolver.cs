using System.Net;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.Enrichment;

/// <summary>
/// <see cref="IGeoIpResolver"/> backed by offline MaxMind GeoLite2 databases.
/// The database paths come from the runtime <see cref="ISecuritySettingsStore"/>,
/// so GeoIP can be enabled/disabled or re-pointed from the dashboard without a
/// restart — the readers are (re)opened lazily whenever the configured paths
/// change. Either the Country or the ASN database (or both) may be supplied;
/// whichever is loaded is queried, the other contributes null. IPs that are not
/// in the database (most notably private LAN ranges) resolve to
/// <see cref="GeoIpInfo.Empty"/> instead of throwing, so the rest of the system
/// keeps working unchanged with or without a database.
/// </summary>
public sealed class MaxMindGeoIpResolver : IGeoIpResolver, IDisposable
{
    private readonly ISecuritySettingsStore _settings;
    private readonly ILogger<MaxMindGeoIpResolver> _logger;
    private readonly object _sync = new();

    private DatabaseReader? _countryReader;
    private DatabaseReader? _asnReader;
    private string? _loadedCountryPath;
    private string? _loadedAsnPath;

    public MaxMindGeoIpResolver(
        ISecuritySettingsStore settings,
        ILogger<MaxMindGeoIpResolver> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsEnabled
    {
        get
        {
            var snapshot = _settings.Current;
            EnsureReaders(snapshot.GeoIpCountryDatabasePath, snapshot.GeoIpAsnDatabasePath);
            return _countryReader is not null || _asnReader is not null;
        }
    }

    public GeoIpInfo Resolve(string ipAddress)
    {
        var snapshot = _settings.Current;
        EnsureReaders(snapshot.GeoIpCountryDatabasePath, snapshot.GeoIpAsnDatabasePath);

        DatabaseReader? countryReader;
        DatabaseReader? asnReader;
        lock (_sync)
        {
            countryReader = _countryReader;
            asnReader = _asnReader;
        }

        if ((countryReader is null && asnReader is null)
            || string.IsNullOrWhiteSpace(ipAddress)
            || !IPAddress.TryParse(ipAddress, out var ip))
        {
            return GeoIpInfo.Empty;
        }

        string? isoCode = null;
        string? countryName = null;
        long? asn = null;
        string? asnOrg = null;

        if (countryReader is not null)
        {
            try
            {
                var country = countryReader.Country(ip);
                isoCode = country.Country.IsoCode;
                countryName = country.Country.Name;
            }
            catch (AddressNotFoundException)
            {
                // Address is not present in the database (e.g. a private LAN IP).
            }
            catch (GeoIP2Exception ex)
            {
                _logger.LogDebug(ex, "GeoIP country lookup failed for {Ip}", ipAddress);
            }
        }

        if (asnReader is not null)
        {
            try
            {
                var asnResponse = asnReader.Asn(ip);
                asn = asnResponse.AutonomousSystemNumber;
                asnOrg = asnResponse.AutonomousSystemOrganization;
            }
            catch (AddressNotFoundException)
            {
                // Address is not present in the ASN database.
            }
            catch (GeoIP2Exception ex)
            {
                _logger.LogDebug(ex, "GeoIP ASN lookup failed for {Ip}", ipAddress);
            }
        }

        if (isoCode is null && countryName is null && asn is null && asnOrg is null)
        {
            return GeoIpInfo.Empty;
        }

        return new GeoIpInfo(isoCode, countryName, asn, asnOrg);
    }

    /// <summary>
    /// (Re)opens the readers when the configured paths change. Cheap no-op on the
    /// hot path: a string comparison under a short lock when nothing changed.
    /// </summary>
    private void EnsureReaders(string? countryPath, string? asnPath)
    {
        var country = NormalizePath(countryPath);
        var asn = NormalizePath(asnPath);

        if (string.Equals(country, _loadedCountryPath, StringComparison.Ordinal)
            && string.Equals(asn, _loadedAsnPath, StringComparison.Ordinal))
        {
            return;
        }

        lock (_sync)
        {
            if (string.Equals(country, _loadedCountryPath, StringComparison.Ordinal)
                && string.Equals(asn, _loadedAsnPath, StringComparison.Ordinal))
            {
                return;
            }

            _countryReader?.Dispose();
            _asnReader?.Dispose();
            _countryReader = OpenReader(country, "country");
            _asnReader = OpenReader(asn, "ASN");
            _loadedCountryPath = country;
            _loadedAsnPath = asn;
        }
    }

    private DatabaseReader? OpenReader(string? path, string kind)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var reader = new DatabaseReader(path);
            _logger.LogInformation("GeoIP {Kind} database loaded from {Path}", kind, path);
            return reader;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to open GeoIP {Kind} database at {Path}", kind, path);
            return null;
        }
    }

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    public void Dispose()
    {
        lock (_sync)
        {
            _countryReader?.Dispose();
            _asnReader?.Dispose();
            _countryReader = null;
            _asnReader = null;
        }
    }
}
