using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Infrastructure.ThreatIntel;

/// <summary>
/// Looks a host up against abuse.ch URLhaus — a free, key-less threat feed of
/// malware-distribution URLs. A host is flagged when URLhaus reports one or
/// more known-malicious URLs for it. Never throws: errors and timeouts return
/// a clean verdict so detection never breaks on a feed hiccup.
/// </summary>
public class UrlhausThreatIntelClient : IThreatIntelClient
{
    private const string Endpoint = "https://urlhaus-api.abuse.ch/v1/host/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ThreatIntelOptions _options;
    private readonly ILogger<UrlhausThreatIntelClient> _logger;

    public UrlhausThreatIntelClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ThreatIntelOptions> options,
        ILogger<UrlhausThreatIntelClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Source => "URLhaus";

    public bool IsEnabled => _options.UrlhausEnabled;

    public async Task<ThreatVerdict> CheckDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(domain))
        {
            return ThreatVerdict.Clean(domain, Source);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("threatintel");
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("host", domain)
            });

            using var response = await client.PostAsync(new Uri(Endpoint), content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            var status = root.TryGetProperty("query_status", out var statusEl)
                ? statusEl.GetString()
                : null;

            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            var urlCount = 0;
            if (root.TryGetProperty("url_count", out var countEl))
            {
                _ = countEl.ValueKind == JsonValueKind.String
                    ? int.TryParse(countEl.GetString(), out urlCount)
                    : countEl.TryGetInt32(out urlCount);
            }

            if (urlCount <= 0)
            {
                return ThreatVerdict.Clean(domain, Source);
            }

            var threat = root.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Array
                && urlsEl.GetArrayLength() > 0
                && urlsEl[0].TryGetProperty("threat", out var threatEl)
                ? threatEl.GetString()
                : "malware";

            return new ThreatVerdict(domain, true, Source, $"{urlCount} known-malicious URL(s); threat={threat}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "URLhaus lookup for {Domain} failed", domain);
            return ThreatVerdict.Clean(domain, Source);
        }
    }
}
