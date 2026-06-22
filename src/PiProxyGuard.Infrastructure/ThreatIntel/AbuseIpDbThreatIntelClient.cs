using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.ThreatIntel;

/// <summary>
/// Looks a domain up through AbuseIPDB. AbuseIPDB scores IP addresses rather
/// than host names, so the domain is first resolved via DNS and the resulting
/// address is checked against <c>GET /api/v2/check</c>. A host is flagged when
/// the abuse-confidence score meets the configured threshold. Reads its API key
/// and threshold from the runtime <see cref="ISecuritySettingsStore"/> so it
/// self-disables until a key is configured and can be enabled from the dashboard
/// without a restart. Never throws — DNS failures, errors and timeouts return a
/// clean verdict.
/// </summary>
public class AbuseIpDbThreatIntelClient : IThreatIntelClient
{
    private const string Endpoint = "https://api.abuseipdb.com/api/v2/check";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecuritySettingsStore _settings;
    private readonly ILogger<AbuseIpDbThreatIntelClient> _logger;

    public AbuseIpDbThreatIntelClient(
        IHttpClientFactory httpClientFactory,
        ISecuritySettingsStore settings,
        ILogger<AbuseIpDbThreatIntelClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public string Source => "AbuseIPDB";

    public bool IsEnabled => _settings.Current.AbuseIpDbConfigured;

    public async Task<ThreatVerdict> CheckDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        var snapshot = _settings.Current;
        var apiKey = snapshot.AbuseIpDbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(domain))
        {
            return ThreatVerdict.Clean(domain, Source);
        }

        try
        {
            var ip = await ResolveAsync(domain, cancellationToken);
            if (ip is null)
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            var threshold = Math.Clamp(snapshot.AbuseIpDbScoreThreshold, 1, 100);
            var client = _httpClientFactory.CreateClient("threatintel");
            var url = $"{Endpoint}?ipAddress={Uri.EscapeDataString(ip)}&maxAgeInDays=90";
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            request.Headers.Add("Key", apiKey);
            request.Headers.Add("Accept", "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("abuseConfidenceScore", out var scoreEl)
                || scoreEl.ValueKind != JsonValueKind.Number
                || !scoreEl.TryGetInt32(out var score))
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            if (score < threshold)
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            var reports = data.TryGetProperty("totalReports", out var reportsEl)
                          && reportsEl.TryGetInt32(out var r)
                ? r
                : 0;

            return new ThreatVerdict(domain, true, Source,
                $"{ip} abuse-confidence {score}% (>= {threshold}%), {reports} report(s)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AbuseIPDB lookup for {Domain} failed", domain);
            return ThreatVerdict.Clean(domain, Source);
        }
    }

    private async Task<string?> ResolveAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, cancellationToken);
            // Prefer IPv4, fall back to the first address returned.
            var preferred = Array.Find(addresses,
                a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return (preferred ?? (addresses.Length > 0 ? addresses[0] : null))?.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "DNS resolution for {Domain} failed", domain);
            return null;
        }
    }
}
