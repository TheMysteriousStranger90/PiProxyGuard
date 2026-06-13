using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;

namespace PiProxyGuard.Domain.Abstractions.Repositories;

/// <summary>Identifies an open alert by client + type, for de-duplication.</summary>
public sealed record AlertKey(string ClientIp, AlertType Type);

/// <summary>
/// Access to suspicious-activity alerts.
/// </summary>
public interface IAlertRepository : IRepository<SuspiciousActivityAlert>
{
    /// <summary>Lists alerts newest-first, optionally only the unacknowledged ones.</summary>
    Task<IReadOnlyList<SuspiciousActivityAlert>> GetAlertsAsync(
        bool onlyUnacknowledged, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keys (client + type) of unacknowledged alerts detected since the given
    /// instant — used to suppress duplicate alerts within the suppression window.
    /// </summary>
    Task<IReadOnlyList<AlertKey>> GetRecentOpenAlertKeysAsync(
        DateTime sinceUtc, CancellationToken cancellationToken = default);
}
