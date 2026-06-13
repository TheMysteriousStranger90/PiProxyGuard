namespace PiProxyGuard.Domain.Abstractions;

/// <summary>Result of a threat-intelligence lookup for a domain.</summary>
/// <param name="Domain">The domain that was checked.</param>
/// <param name="IsMalicious">True when at least one source flagged it.</param>
/// <param name="Source">Which provider produced the verdict.</param>
/// <param name="Details">Free-form context (threat type, reference, ...).</param>
public sealed record ThreatVerdict(string Domain, bool IsMalicious, string Source, string? Details)
{
    public static ThreatVerdict Clean(string domain, string source) => new(domain, false, source, null);
}

/// <summary>
/// Checks a domain against an external threat-intelligence feed. The default
/// free implementation uses URLhaus; commercial providers (VirusTotal,
/// AbuseIPDB) plug in behind the same interface once an API key is supplied.
/// Implementations never throw — a failed lookup returns a clean verdict.
/// </summary>
public interface IThreatIntelClient
{
    /// <summary>Provider name, for logging and the API response.</summary>
    string Source { get; }

    /// <summary>True when the provider is configured and usable.</summary>
    bool IsEnabled { get; }

    /// <summary>Looks the domain up. Returns a clean verdict when disabled or on error.</summary>
    Task<ThreatVerdict> CheckDomainAsync(string domain, CancellationToken cancellationToken = default);
}
