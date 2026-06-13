using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Squid;

namespace PiProxyGuard.Infrastructure.Blocklists;

public record BlocklistUpdateResult(int TotalActiveDomains, int FeedDomains, bool AclRewritten);

/// <summary>
/// Downloads all configured blocklist feeds, synchronizes them into the
/// database (keeping manual/auto entries intact) and rewrites the Squid
/// ACL file when the effective set of domains changed. Persistence goes
/// through <see cref="IUnitOfWork"/> and the blocklist repository.
/// </summary>
public class BlocklistUpdater
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SquidAclWriter _aclWriter;
    private readonly BlocklistOptions _options;
    private readonly ILogger<BlocklistUpdater> _logger;

    public BlocklistUpdater(
        IHttpClientFactory httpClientFactory,
        IUnitOfWork unitOfWork,
        SquidAclWriter aclWriter,
        IOptions<BlocklistOptions> options,
        ILogger<BlocklistUpdater> logger)
    {
        _httpClientFactory = httpClientFactory;
        _unitOfWork = unitOfWork;
        _aclWriter = aclWriter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BlocklistUpdateResult> UpdateAsync(CancellationToken cancellationToken)
    {
        var feedDomains = await DownloadFeedsAsync(cancellationToken);
        await SyncFeedDomainsAsync(feedDomains, cancellationToken);

        await SweepExpiredAutoBlocksAsync(cancellationToken);

        var activeDomains = await _unitOfWork.BlockedDomains.GetActiveDomainNamesAsync(cancellationToken);

        // The allowlist always wins: never emit an allowlisted domain to the ACL.
        var allowlist = await _unitOfWork.AllowedDomains.GetAllDomainNamesAsync(cancellationToken);
        var effectiveDomains = FilterAllowlisted(activeDomains, allowlist);

        var aclRewritten = await _aclWriter.WriteIfChangedAsync(effectiveDomains, cancellationToken);

        _logger.LogInformation(
            "Blocklist update finished: {Total} active domains ({FromFeeds} from feeds, {Allowed} allowlisted), ACL rewritten: {Rewritten}",
            effectiveDomains.Count, feedDomains.Count, activeDomains.Count - effectiveDomains.Count, aclRewritten);

        return new BlocklistUpdateResult(effectiveDomains.Count, feedDomains.Count, aclRewritten);
    }

    private async Task<HashSet<string>> DownloadFeedsAsync(CancellationToken cancellationToken)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var client = _httpClientFactory.CreateClient("blocklists");

        foreach (var feed in _options.Feeds)
        {
            try
            {
                var content = await client.GetStringAsync(feed.Url, cancellationToken);
                var source = CreateSource(feed);
                var count = 0;

                foreach (var domain in source.ExtractDomains(content))
                {
                    if (domains.Add(domain))
                    {
                        count++;
                    }
                }

                _logger.LogInformation("Feed {Url}: {Count} new domains", feed.Url, count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken feed must not break the whole update cycle.
                _logger.LogWarning(ex, "Failed to download or parse feed {Url}", feed.Url);
            }
        }

        return domains;
    }

    private static Domain.Abstractions.IBlocklistSource CreateSource(BlocklistFeedOptions feed) =>
        feed.Format.ToLowerInvariant() switch
        {
            "html" => new HtmlBlocklistSource(
                feed.CssSelector ?? throw new InvalidOperationException(
                    $"Feed {feed.Url} uses the html format but has no CssSelector configured.")),
            _ => new PlainTextBlocklistSource(feed.Format)
        };

    private async Task SyncFeedDomainsAsync(HashSet<string> feedDomains, CancellationToken cancellationToken)
    {
        if (_options.Feeds.Count == 0)
        {
            return;
        }

        var repository = _unitOfWork.BlockedDomains;
        var existing = await repository.GetFeedEntriesAsync(cancellationToken);

        // Remove feed entries that disappeared from all feeds.
        var stale = existing.Values.Where(d => !feedDomains.Contains(d.Domain)).ToList();
        repository.RemoveRange(stale);

        // Add new feed entries (manual/auto entries take precedence and are not duplicated).
        var nonFeedDomains = await repository.GetNonFeedDomainNamesAsync(cancellationToken);
        var occupied = nonFeedDomains.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var domain in feedDomains)
        {
            if (!existing.ContainsKey(domain) && !occupied.Contains(domain))
            {
                repository.Add(new BlockedDomain
                {
                    Domain = domain,
                    Source = BlockSource.Feed,
                    Reason = "blocklist feed",
                    CreatedAtUtc = now,
                    IsActive = true
                });
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task SweepExpiredAutoBlocksAsync(CancellationToken cancellationToken)
    {
        var expired = await _unitOfWork.BlockedDomains.GetExpiredAutoBlocksAsync(DateTime.UtcNow, cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        _unitOfWork.BlockedDomains.RemoveRange(expired);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Released {Count} expired auto-block(s)", expired.Count);
    }

    private static IReadOnlyList<string> FilterAllowlisted(
        IReadOnlyList<string> domains, IReadOnlyList<string> allowlist)
    {
        if (allowlist.Count == 0)
        {
            return domains;
        }

        var allowSet = allowlist.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return domains.Where(d => !IsAllowlisted(d, allowSet)).ToList();
    }

    private static bool IsAllowlisted(string domain, HashSet<string> allowlist)
    {
        var current = domain;
        while (!string.IsNullOrEmpty(current))
        {
            if (allowlist.Contains(current))
            {
                return true;
            }

            var dot = current.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
            {
                break;
            }

            current = current[(dot + 1)..];
        }

        return false;
    }
}
