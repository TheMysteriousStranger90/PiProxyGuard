using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Domain.Abstractions.Repositories;

/// <summary>
/// Access to the upstream-tunnel list — domains routed through the configured
/// parent proxy instead of going out directly.
/// </summary>
public interface ITunneledDomainRepository : IRepository<TunneledDomain>
{
    /// <summary>Lists tunnel entries, optionally filtered by substring. Ordered by domain.</summary>
    Task<IReadOnlyList<TunneledDomain>> SearchAsync(
        string? search, int count, CancellationToken cancellationToken = default);

    /// <summary>Finds a single entry by its exact (normalized) domain name.</summary>
    Task<TunneledDomain?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>All tunneled domain names — used to generate the upstream-tunnel ACL.</summary>
    Task<IReadOnlyList<string>> GetAllDomainNamesAsync(CancellationToken cancellationToken = default);
}
