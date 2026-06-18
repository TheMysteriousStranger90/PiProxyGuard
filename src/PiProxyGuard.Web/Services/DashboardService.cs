using PiProxyGuard.Web.Models;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Domain.Statistics;
using PiProxyGuard.Infrastructure.Blocklists;
using PiProxyGuard.Infrastructure.Diagnostics;

namespace PiProxyGuard.Web.Services;

/// <summary>
/// Facade the Blazor dashboard calls instead of injecting scoped repositories
/// straight into components. Each method opens its own DI scope (and therefore
/// its own <see cref="IUnitOfWork"/>/DbContext), runs one operation and disposes
/// it — so a Blazor Server circuit never shares a non-thread-safe DbContext
/// across overlapping renders.
/// </summary>
public sealed class DashboardService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DashboardService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    // ---- statistics -------------------------------------------------------

    public Task<TrafficSummary> GetSummaryAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetTrafficSummaryAsync(fromUtc, toUtc, ct));

    public Task<IReadOnlyList<HostTraffic>> GetTopHostsAsync(DateTime fromUtc, DateTime toUtc, int count, CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetTopHostsAsync(fromUtc, toUtc, count, ct));

    public Task<IReadOnlyList<ClientTraffic>> GetTopClientsAsync(DateTime fromUtc, DateTime toUtc, int count, CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetTopClientsAsync(fromUtc, toUtc, count, ct));

    public Task<IReadOnlyList<TimelineBucket>> GetTimelineAsync(DateTime fromUtc, DateTime toUtc, TimelineInterval interval, CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetTimelineAsync(fromUtc, toUtc, interval, ct));

    public Task<IReadOnlyList<StatusCodeCount>> GetStatusCodesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetStatusCodeBreakdownAsync(fromUtc, toUtc, ct));

    public async Task<List<CategoryTrafficDto>> GetCategoriesAsync(DateTime fromUtc, DateTime toUtc, int sampleHosts, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var categorizer = scope.ServiceProvider.GetRequiredService<IDomainCategorizer>();
        var hosts = await uow.ProxyLogs.GetTopHostsAsync(fromUtc, toUtc, sampleHosts, ct).ConfigureAwait(false);
        return hosts
            .GroupBy(h => categorizer.Categorize(h.Host))
            .Select(g => new CategoryTrafficDto(g.Key.ToString(), g.Sum(x => x.Requests), g.Sum(x => x.Bytes)))
            .OrderByDescending(c => c.Requests)
            .ToList();
    }

    // ---- blocklist --------------------------------------------------------

    public async Task<List<BlockedDomainDto>> GetBlockedAsync(string? source, string? search, int count, CancellationToken ct = default)
    {
        BlockSource? parsed = null;
        if (!string.IsNullOrEmpty(source) && Enum.TryParse<BlockSource>(source, true, out var value))
        {
            parsed = value;
        }

        return await WithUnitOfWork(async uow =>
        {
            var domains = await uow.BlockedDomains.SearchAsync(parsed, search, count, ct).ConfigureAwait(false);
            return domains
                .Select(d => new BlockedDomainDto(d.Id, d.Domain, d.Source.ToString(), d.Reason, d.CreatedAtUtc, d.IsActive, d.ExpiresAtUtc))
                .ToList();
        }).ConfigureAwait(false);
    }

    public async Task<MutationResult> AddBlockedAsync(string rawDomain, string? reason, int? expiresInHours, CancellationToken ct = default)
    {
        var domain = DomainUtils.NormalizeDomain(rawDomain);
        if (domain is null)
        {
            return MutationResult.Fail($"'{rawDomain}' is not a valid domain.");
        }

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var updater = scope.ServiceProvider.GetRequiredService<BlocklistUpdater>();

        if (await uow.BlockedDomains.FindByDomainAsync(domain, ct).ConfigureAwait(false) is not null)
        {
            return MutationResult.Fail($"'{domain}' is already blocked.");
        }

        uow.BlockedDomains.Add(new BlockedDomain
        {
            Domain = domain,
            Source = BlockSource.Manual,
            Reason = reason,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            ExpiresAtUtc = expiresInHours is > 0 ? DateTime.UtcNow.AddHours(expiresInHours.Value) : null
        });
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        await updater.UpdateAsync(ct).ConfigureAwait(false);
        return MutationResult.Ok();
    }

    public async Task<bool> RemoveBlockedAsync(long id, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var updater = scope.ServiceProvider.GetRequiredService<BlocklistUpdater>();

        var entity = await uow.BlockedDomains.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        var domain = entity.Domain;
        var feedManaged = entity.Source is BlockSource.Feed or BlockSource.Auto;

        uow.BlockedDomains.Remove(entity);

        // Feed/auto entries are re-created on the next feed sync unless we record an
        // explicit "don't block" decision. Allowlisting the domain makes the removal
        // stick (and the domain moves to the Allowlist tab, where it can be undone).
        if (feedManaged &&
            await uow.AllowedDomains.FindByDomainAsync(domain, ct).ConfigureAwait(false) is null)
        {
            uow.AllowedDomains.Add(new AllowedDomain
            {
                Domain = domain,
                Reason = "Removed from blocklist",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        await updater.UpdateAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<BlocklistUpdateResult> RefreshFeedsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var updater = scope.ServiceProvider.GetRequiredService<BlocklistUpdater>();
        return await updater.UpdateAsync(ct).ConfigureAwait(false);
    }

    // ---- allowlist --------------------------------------------------------

    public async Task<List<AllowedDomainDto>> GetAllowedAsync(string? search, int count, CancellationToken ct = default) =>
        await WithUnitOfWork(async uow =>
        {
            var domains = await uow.AllowedDomains.SearchAsync(search, count, ct).ConfigureAwait(false);
            return domains.Select(d => new AllowedDomainDto(d.Id, d.Domain, d.Reason, d.CreatedAtUtc)).ToList();
        }).ConfigureAwait(false);

    public async Task<MutationResult> AddAllowedAsync(string rawDomain, string? reason, CancellationToken ct = default)
    {
        var domain = DomainUtils.NormalizeDomain(rawDomain);
        if (domain is null)
        {
            return MutationResult.Fail($"'{rawDomain}' is not a valid domain.");
        }

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var updater = scope.ServiceProvider.GetRequiredService<BlocklistUpdater>();

        if (await uow.AllowedDomains.FindByDomainAsync(domain, ct).ConfigureAwait(false) is not null)
        {
            return MutationResult.Fail($"'{domain}' is already allowlisted.");
        }

        uow.AllowedDomains.Add(new AllowedDomain { Domain = domain, Reason = reason, CreatedAtUtc = DateTime.UtcNow });
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        await updater.UpdateAsync(ct).ConfigureAwait(false);
        return MutationResult.Ok();
    }

    public async Task<bool> RemoveAllowedAsync(long id, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var updater = scope.ServiceProvider.GetRequiredService<BlocklistUpdater>();

        var entity = await uow.AllowedDomains.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        uow.AllowedDomains.Remove(entity);
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        await updater.UpdateAsync(ct).ConfigureAwait(false);
        return true;
    }

    // ---- alerts -----------------------------------------------------------

    public async Task<List<AlertDto>> GetAlertsAsync(bool onlyUnacknowledged, int count, CancellationToken ct = default) =>
        await WithUnitOfWork(async uow =>
        {
            var alerts = await uow.Alerts.GetAlertsAsync(onlyUnacknowledged, count, ct).ConfigureAwait(false);
            return alerts
                .Select(a => new AlertDto(a.Id, a.ClientIp, a.Type.ToString(), a.Description,
                    a.WindowStartUtc, a.WindowEndUtc, a.DetectedAtUtc, a.IsAcknowledged))
                .ToList();
        }).ConfigureAwait(false);

    public async Task<bool> AcknowledgeAlertAsync(long id, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var alert = await uow.Alerts.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (alert is null)
        {
            return false;
        }

        alert.IsAcknowledged = true;
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Highest alert id currently stored (0 when there are none). Used by the live monitor.</summary>
    public async Task<long> GetLatestAlertIdAsync(CancellationToken ct = default)
    {
        var latest = await WithUnitOfWork(uow => uow.Alerts.GetAlertsAsync(false, 1, ct)).ConfigureAwait(false);
        return latest.Count > 0 ? latest[0].Id : 0L;
    }

    // ---- diagnostics ------------------------------------------------------

    public async Task<DiagnosticsReport> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var diagnostics = scope.ServiceProvider.GetRequiredService<SystemDiagnostics>();
        return await diagnostics.RunAsync(ct).ConfigureAwait(false);
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<T> WithUnitOfWork<T>(Func<IUnitOfWork, Task<T>> operation)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        return await operation(uow).ConfigureAwait(false);
    }
}

/// <summary>Result of a create/update operation surfaced to the UI.</summary>
public sealed record MutationResult(bool Success, string? Error)
{
    public static MutationResult Ok() => new(true, null);
    public static MutationResult Fail(string error) => new(false, error);
}
