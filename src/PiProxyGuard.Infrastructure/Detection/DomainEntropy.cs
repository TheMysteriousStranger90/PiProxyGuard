namespace PiProxyGuard.Infrastructure.Detection;

/// <summary>
/// Pure helpers for scoring how "machine-generated" a domain name looks.
/// Algorithmically generated domains (DGA), used by malware to reach
/// command-and-control servers, tend to have high Shannon entropy in their
/// most significant label (e.g. "x7gq9z2v1k8w3p.com"). No state, no I/O —
/// trivially unit-testable.
/// </summary>
public static class DomainEntropy
{
    /// <summary>
    /// Shannon entropy (bits per character) of a string. Returns 0 for empty.
    /// </summary>
    public static double ShannonEntropy(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0.0;
        }

        var counts = new Dictionary<char, int>();
        foreach (var c in value)
        {
            counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
        }

        double entropy = 0.0;
        double length = value.Length;
        foreach (var count in counts.Values)
        {
            var p = count / length;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    /// <summary>
    /// The most significant label of a host (the one before the public suffix,
    /// approximated as the second-to-last dotted label). For "abc.example.co.uk"
    /// returns "example"; for "x7gq9z.com" returns "x7gq9z".
    /// </summary>
    public static string SignificantLabel(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return labels.Length switch
        {
            0 => string.Empty,
            1 => labels[0],
            _ => labels[^2]
        };
    }

    /// <summary>
    /// True when the host's significant label is long enough and has entropy
    /// above the threshold — i.e. it looks algorithmically generated.
    /// </summary>
    public static bool LooksAlgorithmic(string host, double entropyThreshold, int minLabelLength)
    {
        var label = SignificantLabel(host);
        if (label.Length < minLabelLength)
        {
            return false;
        }

        return ShannonEntropy(label) >= entropyThreshold;
    }
}
