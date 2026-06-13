using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Domain.Abstractions.Repositories;

/// <summary>
/// Access to the allowlist — domains that must never be blocked. The
/// allowlist always overrides the blocklist.
/// </summary>
public interface IAllowedDomainRepository : IRepository<AllowedDomain>
{
    /// <summary>Lists allowlist entries, optionally filtered by substring. Ordered by domain.</summary>
    Task<IReadOnlyList<AllowedDomain>> SearchAsync(
        string? search, int count, CancellationToken cancellationToken = default);

    /// <summary>Finds a single entry by its exact (normalized) domain name.</summary>
    Task<AllowedDomain?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>All allowlisted domain names — used to filter the generated ACL.</summary>
    Task<IReadOnlyList<string>> GetAllDomainNamesAsync(CancellationToken cancellationToken = default);
}
