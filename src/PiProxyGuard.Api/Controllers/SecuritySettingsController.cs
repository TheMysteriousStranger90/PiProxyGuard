using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Api.Controllers;

/// <summary>
/// Reads and updates the runtime security/integration settings (GeoIP database
/// paths, threat-intel providers, the scheduled digest and the background scan)
/// — the REST equivalent of the dashboard Settings page. API keys are masked on
/// read and a blank key on write keeps the stored value.
/// </summary>
[ApiController]
[Route("api/security")]
public class SecuritySettingsController : ControllerBase
{
    private readonly ISecuritySettingsStore _store;

    public SecuritySettingsController(ISecuritySettingsStore store) => _store = store;

    /// <summary>Returns the current security settings (API keys masked).</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<SecuritySettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var snapshot = await _store.GetAsync(cancellationToken);
        return ToDto(snapshot);
    }

    /// <summary>Replaces the security settings. Blank API keys keep the stored value.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<SecuritySettingsDto>> UpdateSettings(
        [FromBody] UpdateSecuritySettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await _store.GetAsync(cancellationToken);

        var virusTotalKey = string.IsNullOrWhiteSpace(request.VirusTotalApiKey)
            ? current.VirusTotalApiKey
            : request.VirusTotalApiKey.Trim();
        var abuseKey = string.IsNullOrWhiteSpace(request.AbuseIpDbApiKey)
            ? current.AbuseIpDbApiKey
            : request.AbuseIpDbApiKey.Trim();

        var snapshot = current with
        {
            GeoIpCountryDatabasePath = Blank(request.GeoIpCountryDatabasePath),
            GeoIpAsnDatabasePath = Blank(request.GeoIpAsnDatabasePath),
            UrlhausEnabled = request.UrlhausEnabled,
            VirusTotalApiKey = virusTotalKey,
            AbuseIpDbApiKey = abuseKey,
            AbuseIpDbScoreThreshold = Math.Clamp(request.AbuseIpDbScoreThreshold, 1, 100),
            DailyDigestEnabled = request.DailyDigestEnabled,
            DailyReportHour = Math.Clamp(request.DailyReportHour, 0, 23),
            DigestWindowHours = Math.Clamp(request.DigestWindowHours, 1, 720),
            ScanEnabled = request.ScanEnabled,
            ScanIntervalHours = Math.Clamp(request.ScanIntervalHours, 1, 168),
            ScanLookbackHours = Math.Clamp(request.ScanLookbackHours, 1, 720),
            ScanTopDomains = Math.Clamp(request.ScanTopDomains, 1, 1000),
            ScanRequestDelayMs = Math.Clamp(request.ScanRequestDelayMs, 0, 60000),
            ScanAutoBlock = request.ScanAutoBlock,
            ScanAutoBlockTtlHours = Math.Max(0, request.ScanAutoBlockTtlHours)
        };

        await _store.SaveAsync(snapshot, cancellationToken);

        var saved = await _store.GetAsync(cancellationToken);
        return ToDto(saved);
    }

    private static SecuritySettingsDto ToDto(SecuritySettingsSnapshot s) => new(
        s.GeoIpCountryDatabasePath,
        s.GeoIpAsnDatabasePath,
        s.UrlhausEnabled,
        s.VirusTotalConfigured,
        Mask(s.VirusTotalApiKey),
        s.AbuseIpDbConfigured,
        Mask(s.AbuseIpDbApiKey),
        s.AbuseIpDbScoreThreshold,
        s.DailyDigestEnabled,
        s.DailyReportHour,
        s.DigestWindowHours,
        s.ScanEnabled,
        s.ScanIntervalHours,
        s.ScanLookbackHours,
        s.ScanTopDomains,
        s.ScanRequestDelayMs,
        s.ScanAutoBlock,
        s.ScanAutoBlockTtlHours);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Mask(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var trimmed = secret.Trim();
        var tail = trimmed.Length <= 4 ? trimmed : trimmed[^4..];
        return $"\u2022\u2022\u2022{tail}";
    }
}
