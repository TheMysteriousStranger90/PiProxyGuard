using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.ThreatIntel;

/// <summary>
/// Fans a domain lookup out across every configured provider (URLhaus,
/// VirusTotal, AbuseIPDB) and returns the first malicious verdict it finds, so
/// a single positive from any feed is enough to flag a host. Disabled providers
/// are skipped; when none are enabled (or all return clean) a clean verdict is
/// returned. This is the public <see cref="IThreatIntelClient"/> the API and the
/// background scanner consume.
/// </summary>
public class CompositeThreatIntelClient : IThreatIntelClient
{
    private readonly IReadOnlyList<IThreatIntelClient> _providers;

    public CompositeThreatIntelClient(IEnumerable<IThreatIntelClient> providers)
    {
        _providers = providers.ToList();
    }

    public string Source => "Composite";

    public bool IsEnabled => _providers.Any(p => p.IsEnabled);

    public async Task<ThreatVerdict> CheckDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ThreatVerdict.Clean(domain, Source);
        }

        var enabled = _providers.Where(p => p.IsEnabled).ToList();
        if (enabled.Count == 0)
        {
            return ThreatVerdict.Clean(domain, Source);
        }

        var lastSource = Source;
        foreach (var provider in enabled)
        {
            var verdict = await provider.CheckDomainAsync(domain, cancellationToken);
            lastSource = provider.Source;
            if (verdict.IsMalicious)
            {
                return verdict;
            }
        }

        return ThreatVerdict.Clean(domain, lastSource);
    }
}
