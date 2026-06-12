namespace PiProxyGuard.Domain.Abstractions;

/// <summary>
/// Extracts blocked domain names from a downloaded feed payload.
/// Implementations exist per feed format (hosts/plain text, HTML page).
/// </summary>
public interface IBlocklistSource
{
    /// <summary>Format key referenced from configuration ("hosts", "plain", "html").</summary>
    string Format { get; }

    /// <summary>Parses the raw feed content and yields normalized, lower-case domains.</summary>
    IEnumerable<string> ExtractDomains(string content);
}
