using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;

namespace PiProxyGuard.Infrastructure.Blocklists;

/// <summary>
/// Parses plain-text feeds: either a hosts file ("0.0.0.0 ads.example.com")
/// or a simple list of one domain per line. Lines starting with '#', '!'
/// or ';' are treated as comments.
/// </summary>
public class PlainTextBlocklistSource : IBlocklistSource
{
    public PlainTextBlocklistSource(string format) => Format = format;

    public string Format { get; }

    public IEnumerable<string> ExtractDomains(string content)
    {
        using var reader = new StringReader(content);

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] is '#' or '!' or ';')
            {
                continue;
            }

            // Strip inline comments.
            var hashIndex = trimmed.IndexOf('#');
            if (hashIndex >= 0)
            {
                trimmed = trimmed[..hashIndex].Trim();
            }

            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            // hosts format: "<ip> <domain>"; plain format: "<domain>".
            var candidate = parts.Length >= 2 && (parts[0] == "0.0.0.0" || parts[0] == "127.0.0.1" || parts[0] == "::")
                ? parts[1]
                : parts[0];

            if (DomainUtils.NormalizeDomain(candidate) is { } domain)
            {
                yield return domain;
            }
        }
    }
}
