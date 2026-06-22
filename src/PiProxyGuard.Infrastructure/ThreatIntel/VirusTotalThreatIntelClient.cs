using System.Text.Json;
using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.ThreatIntel;

/// <summary>
/// Looks a domain up through the VirusTotal v3 API
/// (<c>GET /api/v3/domains/{domain}</c>). A host is flagged when one or more
/// engines report it malicious (suspicious verdicts are surfaced in the
/// details). Reads its API key from the runtime <see cref="ISecuritySettingsStore"/>
/// so it self-disables until a key is configured — and can be enabled from the
/// dashboard without a restart. Never throws — errors, rate limits and timeouts
/// return a clean verdict.
/// </summary>
public class VirusTotalThreatIntelClient : IThreatIntelClient
{
    private const string Endpoint = "https://www.virustotal.com/api/v3/domains/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecuritySettingsStore _settings;
    private readonly ILogger<VirusTotalThreatIntelClient> _logger;

    public VirusTotalThreatIntelClient(
        IHttpClientFactory httpClientFactory,
        ISecuritySettingsStore settings,
        ILogger<VirusTotalThreatIntelClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public string Source => "VirusTotal";

    public bool IsEnabled => _settings.Current.VirusTotalConfigured;

    public async Task<ThreatVerdict> CheckDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.Current.VirusTotalApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(domain))
        {
            return ThreatVerdict.Clean(domain, Source);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("threatintel");
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(Endpoint + Uri.EscapeDataString(domain)));
            request.Headers.Add("x-apikey", apiKey);
            request.Headers.Add("Accept", "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("attributes", out var attributes)
                || !attributes.TryGetProperty("last_analysis_stats", out var stats))
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            var malicious = ReadInt(stats, "malicious");
            var suspicious = ReadInt(stats, "suspicious");

            if (malicious <= 0 && suspicious <= 0)
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            return new ThreatVerdict(domain, true, Source,
                $"{malicious} engine(s) flagged malicious, {suspicious} suspicious");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "VirusTotal lookup for {Domain} failed", domain);
            return ThreatVerdict.Clean(domain, Source);
        }
    }

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
                                                        && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
}
