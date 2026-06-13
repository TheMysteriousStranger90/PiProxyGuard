using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Categorization;

namespace PiProxyGuard.Infrastructure.Detection;

/// <summary>
/// Offline, deterministic domain categorizer. Matches a host against a curated
/// set of known suffixes and keywords. Intentionally simple — it gives the
/// stats layer a useful coarse breakdown without any network calls or licences.
/// First match wins, evaluated most-specific-first (malware before generic CDN).
/// </summary>
public class RuleBasedDomainCategorizer : IDomainCategorizer
{
    // Suffix matches: host equals the entry or ends with "." + entry.
    private static readonly (string Suffix, DomainCategory Category)[] Suffixes =
    [
        // Advertising
        ("doubleclick.net", DomainCategory.Advertising),
        ("googlesyndication.com", DomainCategory.Advertising),
        ("googleadservices.com", DomainCategory.Advertising),
        ("adnxs.com", DomainCategory.Advertising),
        ("adservice.google.com", DomainCategory.Advertising),
        ("amazon-adsystem.com", DomainCategory.Advertising),
        ("rubiconproject.com", DomainCategory.Advertising),
        ("pubmatic.com", DomainCategory.Advertising),
        ("criteo.com", DomainCategory.Advertising),
        ("taboola.com", DomainCategory.Advertising),
        ("outbrain.com", DomainCategory.Advertising),
        // Tracking / analytics / telemetry
        ("google-analytics.com", DomainCategory.Tracking),
        ("analytics.google.com", DomainCategory.Tracking),
        ("googletagmanager.com", DomainCategory.Tracking),
        ("scorecardresearch.com", DomainCategory.Tracking),
        ("hotjar.com", DomainCategory.Tracking),
        ("mixpanel.com", DomainCategory.Tracking),
        ("segment.io", DomainCategory.Tracking),
        ("branch.io", DomainCategory.Tracking),
        ("crashlytics.com", DomainCategory.Tracking),
        ("app-measurement.com", DomainCategory.Tracking),
        ("sentry.io", DomainCategory.Tracking),
        // Social
        ("facebook.com", DomainCategory.Social),
        ("fbcdn.net", DomainCategory.Social),
        ("instagram.com", DomainCategory.Social),
        ("twitter.com", DomainCategory.Social),
        ("x.com", DomainCategory.Social),
        ("tiktok.com", DomainCategory.Social),
        ("snapchat.com", DomainCategory.Social),
        ("linkedin.com", DomainCategory.Social),
        ("reddit.com", DomainCategory.Social),
        ("pinterest.com", DomainCategory.Social),
        // Streaming
        ("youtube.com", DomainCategory.Streaming),
        ("googlevideo.com", DomainCategory.Streaming),
        ("ytimg.com", DomainCategory.Streaming),
        ("netflix.com", DomainCategory.Streaming),
        ("nflxvideo.net", DomainCategory.Streaming),
        ("spotify.com", DomainCategory.Streaming),
        ("scdn.co", DomainCategory.Streaming),
        ("twitch.tv", DomainCategory.Streaming),
        ("ttvnw.net", DomainCategory.Streaming),
        ("hulu.com", DomainCategory.Streaming),
        ("primevideo.com", DomainCategory.Streaming),
        // CDN / static asset hosts
        ("akamaihd.net", DomainCategory.Cdn),
        ("akamaized.net", DomainCategory.Cdn),
        ("cloudfront.net", DomainCategory.Cdn),
        ("cloudflare.com", DomainCategory.Cdn),
        ("fastly.net", DomainCategory.Cdn),
        ("jsdelivr.net", DomainCategory.Cdn),
        ("cdn.jsdelivr.net", DomainCategory.Cdn),
        ("gstatic.com", DomainCategory.Cdn),
        ("cdninstagram.com", DomainCategory.Cdn),
    ];

    // Keyword matches: any label of the host equals one of these.
    private static readonly (string Keyword, DomainCategory Category)[] Keywords =
    [
        ("ads", DomainCategory.Advertising),
        ("adserver", DomainCategory.Advertising),
        ("adsystem", DomainCategory.Advertising),
        ("telemetry", DomainCategory.Tracking),
        ("analytics", DomainCategory.Tracking),
        ("metrics", DomainCategory.Tracking),
        ("tracking", DomainCategory.Tracking),
        ("cdn", DomainCategory.Cdn),
    ];

    public DomainCategory Categorize(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return DomainCategory.Unknown;
        }

        var normalized = host.Trim().ToLowerInvariant();

        foreach (var (suffix, category) in Suffixes)
        {
            if (normalized == suffix || normalized.EndsWith("." + suffix, StringComparison.Ordinal))
            {
                return category;
            }
        }

        var labels = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (keyword, category) in Keywords)
        {
            if (Array.IndexOf(labels, keyword) >= 0)
            {
                return category;
            }
        }

        return DomainCategory.Unknown;
    }
}
