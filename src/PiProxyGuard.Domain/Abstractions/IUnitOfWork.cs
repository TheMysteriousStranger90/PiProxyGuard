using PiProxyGuard.Domain.Abstractions.Repositories;

namespace PiProxyGuard.Domain.Abstractions;

/// <summary>
/// Coordinates the repositories that share a single database session and
/// commits their staged changes as one atomic unit. Resolve it from the
/// DI container (scoped) and call <see cref="SaveChangesAsync"/> once per
/// logical operation.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Proxy log entries and traffic aggregations.</summary>
    IProxyLogRepository ProxyLogs { get; }

    /// <summary>The aggregated blocklist.</summary>
    IBlockedDomainRepository BlockedDomains { get; }

    /// <summary>Suspicious-activity alerts.</summary>
    IAlertRepository Alerts { get; }

    /// <summary>Per-file log ingestion bookmarks.</summary>
    ILogIngestionStateRepository IngestionStates { get; }

    /// <summary>Persists every staged change in one transaction. Returns rows affected.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
