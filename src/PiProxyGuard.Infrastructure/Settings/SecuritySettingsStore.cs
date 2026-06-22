using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Persistence;

namespace PiProxyGuard.Infrastructure.Settings;

/// <summary>
/// Database-backed <see cref="ISecuritySettingsStore"/>. Keeps the single
/// settings row cached so the (singleton) GeoIP resolver and threat-intel
/// clients can read it cheaply, refreshes it on a short TTL so changes made in
/// the dashboard reach the separate Worker process, and falls back to the
/// <c>GeoIp</c>/<c>ThreatIntel</c>/<c>Reports</c>/<c>ThreatIntelScan</c>
/// configuration sections when no row has been saved yet.
/// </summary>
public sealed class SecuritySettingsStore : ISecuritySettingsStore, IDisposable
{
    /// <summary>Primary key of the single settings row.</summary>
    public const long SingletonId = 1;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(8);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SecuritySettingsSnapshot _fallback;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SecuritySettingsSnapshot? _cached;
    private DateTime _cachedAtUtc;

    public SecuritySettingsStore(
        IServiceScopeFactory scopeFactory,
        IOptions<GeoIpOptions> geoIp,
        IOptions<ThreatIntelOptions> threatIntel,
        IOptions<ReportOptions> reports,
        IOptions<ThreatIntelScanOptions> scan)
    {
        _scopeFactory = scopeFactory;
        _fallback = BuildFallback(geoIp.Value, threatIntel.Value, reports.Value, scan.Value);
    }

    public SecuritySettingsSnapshot Current => _cached ?? _fallback;

    public async Task<SecuritySettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cached;
        if (cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
            {
                return _cached;
            }

            var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
            _cached = snapshot;
            _cachedAtUtc = DateTime.UtcNow;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SecuritySettingsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await dbContext.SecuritySettings
                .FirstOrDefaultAsync(s => s.Id == SingletonId, cancellationToken)
                .ConfigureAwait(false);

            if (row is null)
            {
                row = new SecuritySetting { Id = SingletonId };
                dbContext.SecuritySettings.Add(row);
            }

            ApplyTo(row, snapshot);
            row.UpdatedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _cached = ToSnapshot(row);
            _cachedAtUtc = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SecuritySettingsSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await dbContext.SecuritySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SingletonId, cancellationToken)
            .ConfigureAwait(false);

        return row is not null ? ToSnapshot(row) : _fallback;
    }

    private static void ApplyTo(SecuritySetting row, SecuritySettingsSnapshot s)
    {
        row.GeoIpCountryDatabasePath = NullIfBlank(s.GeoIpCountryDatabasePath);
        row.GeoIpAsnDatabasePath = NullIfBlank(s.GeoIpAsnDatabasePath);

        row.UrlhausEnabled = s.UrlhausEnabled;
        row.VirusTotalApiKey = NullIfBlank(s.VirusTotalApiKey);
        row.AbuseIpDbApiKey = NullIfBlank(s.AbuseIpDbApiKey);
        row.AbuseIpDbScoreThreshold = Math.Clamp(s.AbuseIpDbScoreThreshold, 1, 100);

        row.DailyDigestEnabled = s.DailyDigestEnabled;
        row.DailyReportHour = Math.Clamp(s.DailyReportHour, 0, 23);
        row.DigestWindowHours = Math.Clamp(s.DigestWindowHours, 1, 720);
        row.DigestTitle = string.IsNullOrWhiteSpace(s.DigestTitle) ? "PiProxyGuard daily digest" : s.DigestTitle.Trim();
        row.DigestSeverity = s.DigestSeverity;

        row.ScanEnabled = s.ScanEnabled;
        row.ScanIntervalHours = Math.Clamp(s.ScanIntervalHours, 1, 168);
        row.ScanLookbackHours = Math.Clamp(s.ScanLookbackHours, 1, 720);
        row.ScanTopDomains = Math.Clamp(s.ScanTopDomains, 1, 1000);
        row.ScanRequestDelayMs = Math.Clamp(s.ScanRequestDelayMs, 0, 60000);
        row.ScanAutoBlock = s.ScanAutoBlock;
        row.ScanAutoBlockTtlHours = Math.Max(0, s.ScanAutoBlockTtlHours);
    }

    private static SecuritySettingsSnapshot ToSnapshot(SecuritySetting row) => new(
        NullIfBlank(row.GeoIpCountryDatabasePath),
        NullIfBlank(row.GeoIpAsnDatabasePath),
        row.UrlhausEnabled,
        NullIfBlank(row.VirusTotalApiKey),
        NullIfBlank(row.AbuseIpDbApiKey),
        row.AbuseIpDbScoreThreshold > 0 ? row.AbuseIpDbScoreThreshold : 50,
        row.DailyDigestEnabled,
        row.DailyReportHour,
        row.DigestWindowHours > 0 ? row.DigestWindowHours : 24,
        string.IsNullOrWhiteSpace(row.DigestTitle) ? "PiProxyGuard daily digest" : row.DigestTitle,
        row.DigestSeverity,
        row.ScanEnabled,
        row.ScanIntervalHours > 0 ? row.ScanIntervalHours : 6,
        row.ScanLookbackHours > 0 ? row.ScanLookbackHours : 24,
        row.ScanTopDomains > 0 ? row.ScanTopDomains : 50,
        row.ScanRequestDelayMs >= 0 ? row.ScanRequestDelayMs : 1500,
        row.ScanAutoBlock,
        Math.Max(0, row.ScanAutoBlockTtlHours));

    private static SecuritySettingsSnapshot BuildFallback(
        GeoIpOptions geo, ThreatIntelOptions ti, ReportOptions reports, ThreatIntelScanOptions scan) => new(
        NullIfBlank(geo.CountryDatabasePath),
        NullIfBlank(geo.AsnDatabasePath),
        ti.UrlhausEnabled,
        NullIfBlank(ti.VirusTotalApiKey),
        NullIfBlank(ti.AbuseIpDbApiKey),
        ti.AbuseIpDbScoreThreshold > 0 ? ti.AbuseIpDbScoreThreshold : 50,
        reports.DailyDigestEnabled,
        reports.DailyReportHour,
        reports.WindowHours > 0 ? reports.WindowHours : 24,
        string.IsNullOrWhiteSpace(reports.Title) ? "PiProxyGuard daily digest" : reports.Title,
        (NotificationSeverity)(int)reports.Severity,
        scan.Enabled,
        scan.IntervalHours > 0 ? scan.IntervalHours : 6,
        scan.LookbackHours > 0 ? scan.LookbackHours : 24,
        scan.TopDomains > 0 ? scan.TopDomains : 50,
        scan.RequestDelayMs >= 0 ? scan.RequestDelayMs : 1500,
        scan.AutoBlock,
        Math.Max(0, scan.AutoBlockTtlHours));

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => _gate.Dispose();
}
