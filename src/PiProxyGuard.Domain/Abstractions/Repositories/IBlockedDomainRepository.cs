using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;

namespace PiProxyGuard.Domain.Abstractions.Repositories;

/// <summary>
/// Access to the aggregated blocklist (manual, feed and auto entries).
/// </summary>
public interface IBlockedDomainRepository : IRepository<BlockedDomain>
{
    /// <summary>Filtered, paged browse used by the API. Ordered by domain.</summary>
    Task<IReadOnlyList<BlockedDomain>> SearchAsync(
        BlockSource? source, string? search, int count, CancellationToken cancellationToken = default);

    /// <summary>Finds a single entry by its exact (normalized) domain name.</summary>
    Task<BlockedDomain?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>All active domain names, ordered — the source for the Squid ACL file.</summary>
    Task<IReadOnlyList<string>> GetActiveDomainNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Existing feed entries keyed by domain (for incremental feed sync).</summary>
    Task<IReadOnlyDictionary<string, BlockedDomain>> GetFeedEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Domains owned by manual/auto entries — they take precedence over feeds.</summary>
    Task<IReadOnlyList<string>> GetNonFeedDomainNamesAsync(CancellationToken cancellationToken = default);
}
