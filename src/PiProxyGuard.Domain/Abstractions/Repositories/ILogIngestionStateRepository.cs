using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Domain.Abstractions.Repositories;

/// <summary>
/// Access to per-file log ingestion bookmarks (byte offset + rotation
/// fingerprint) so the worker can resume after a restart.
/// </summary>
public interface ILogIngestionStateRepository : IRepository<LogIngestionState>
{
    /// <summary>Returns the bookmark for a log file path, or null when none exists yet.</summary>
    Task<LogIngestionState?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>All ingestion bookmarks — surfaced by the API for an operational view.</summary>
    Task<IReadOnlyList<LogIngestionState>> GetAllAsync(CancellationToken cancellationToken = default);
}
