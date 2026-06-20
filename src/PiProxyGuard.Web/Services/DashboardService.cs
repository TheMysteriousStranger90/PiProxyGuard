using PiProxyGuard.Web.Models;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Domain.Statistics;
using PiProxyGuard.Infrastructure.Blocklists;
using PiProxyGuard.Infrastructure.Diagnostics;
using PiProxyGuard.Infrastructure.Tunneling;

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

    public Task<IReadOnlyList<HostTraffic>> GetTopHostsAsync(DateTime fromUtc, DateTime toUtc, int count,
        CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetTopHostsAsync(fromUtc, toUtc, count, ct));

    public Task<IReadOnlyList<ClientTraffic>> GetTopClientsAsync(DateTime fromUtc, DateTime toUtc, int count,
        CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetTopClientsAsync(fromUtc, toUtc, count, ct));

    public Task<IReadOnlyList<TimelineBucket>> GetTimelineAsync(DateTime fromUtc, DateTime toUtc,
        TimelineInterval interval, CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetTimelineAsync(fromUtc, toUtc, interval, ct));

    public Task<IReadOnlyList<StatusCodeCount>> GetStatusCodesAsync(DateTime fromUtc, DateTime toUtc,
        CancellationToken ct = default) =>
        WithUnitOfWork(uow => uow.ProxyLogs.GetStatusCodeBreakdownAsync(fromUtc, toUtc, ct));

    public async Task<List<CategoryTrafficDto>> GetCategoriesAsync(DateTime fromUtc, DateTime toUtc, int sampleHosts,
        CancellationToken ct = default)
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

    public async Task<List<BlockedDomainDto>> GetBlockedAsync(string? source, string? search, int count,
        CancellationToken ct = default)
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
                .Select(d => new BlockedDomainDto(d.Id, d.Domain, d.Source.ToString(), d.Reason, d.CreatedAtUtc,
                    d.IsActive, d.ExpiresAtUtc))
                .ToList();
        }).ConfigureAwait(false);
    }

    public async Task<MutationResult> AddBlockedAsync(string rawDomain, string? reason, int? expiresInHours,
        CancellationToken ct = default)
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

    public async Task<List<AllowedDomainDto>>
        GetAllowedAsync(string? search, int count, CancellationToken ct = default) =>
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

    // ---- upstream tunnel --------------------------------------------------

    public async Task<List<TunneledDomainDto>>
        GetTunneledAsync(string? search, int count, CancellationToken ct = default) =>
        await WithUnitOfWork(async uow =>
        {
            var domains = await uow.TunneledDomains.SearchAsync(search, count, ct).ConfigureAwait(false);
            return domains.Select(d => new TunneledDomainDto(d.Id, d.Domain, d.Reason, d.CreatedAtUtc)).ToList();
        }).ConfigureAwait(false);

    public async Task<MutationResult> AddTunneledAsync(string rawDomain, string? reason, CancellationToken ct = default)
    {
        var domain = DomainUtils.NormalizeDomain(rawDomain);
        if (domain is null)
        {
            return MutationResult.Fail($"'{rawDomain}' is not a valid domain.");
        }

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var updater = scope.ServiceProvider.GetRequiredService<TunnelAclUpdater>();

        if (await uow.TunneledDomains.FindByDomainAsync(domain, ct).ConfigureAwait(false) is not null)
        {
            return MutationResult.Fail($"'{domain}' is already tunneled.");
        }

        uow.TunneledDomains.Add(new TunneledDomain { Domain = domain, Reason = reason, CreatedAtUtc = DateTime.UtcNow });
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        await updater.UpdateAsync(ct).ConfigureAwait(false);
        return MutationResult.Ok();
    }

    public async Task<bool> RemoveTunneledAsync(long id, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var updater = scope.ServiceProvider.GetRequiredService<TunnelAclUpdater>();

        var entity = await uow.TunneledDomains.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        uow.TunneledDomains.Remove(entity);
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        await updater.UpdateAsync(ct).ConfigureAwait(false);
        return true;
    }

    // ---- alerts -----------------------------------------------------------

    public async Task<List<AlertDto>>
        GetAlertsAsync(bool onlyUnacknowledged, int count, CancellationToken ct = default) =>
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

    // ---- notifications ----------------------------------------------------

    public async Task<NotificationSettingsViewModel> GetNotificationSettingsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationSettingsStore>();
        var s = await store.GetAsync(ct).ConfigureAwait(false);

        return new NotificationSettingsViewModel(
            s.MinimumSeverity.ToString(),
            s.TelegramEnabled,
            !string.IsNullOrWhiteSpace(s.TelegramBotToken),
            Mask(s.TelegramBotToken),
            s.TelegramChatId,
            s.EmailEnabled,
            s.EmailHost,
            s.EmailPort,
            s.EmailUseSsl,
            s.EmailUsername,
            !string.IsNullOrWhiteSpace(s.EmailPassword),
            s.EmailFrom,
            s.EmailTo);
    }

    public async Task<MutationResult> SaveNotificationSettingsAsync(NotificationSettingsInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationSettingsStore>();
        var current = await store.GetAsync(ct).ConfigureAwait(false);

        var severity = Enum.TryParse<NotificationSeverity>(input.MinimumSeverity, true, out var parsed)
            ? parsed
            : current.MinimumSeverity;

        // A blank secret means "keep the stored one" — the browser never sees it.
        var token = string.IsNullOrWhiteSpace(input.TelegramBotToken)
            ? current.TelegramBotToken
            : input.TelegramBotToken.Trim();
        var password = string.IsNullOrWhiteSpace(input.EmailPassword)
            ? current.EmailPassword
            : input.EmailPassword.Trim();

        if (input.TelegramEnabled &&
            (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(input.TelegramChatId)))
        {
            return MutationResult.Fail("Telegram needs a bot token and a chat id.");
        }

        if (input.EmailEnabled &&
            (string.IsNullOrWhiteSpace(input.EmailHost) ||
             string.IsNullOrWhiteSpace(input.EmailFrom) ||
             string.IsNullOrWhiteSpace(input.EmailTo)))
        {
            return MutationResult.Fail("E-mail needs a host, a from address and at least one recipient.");
        }

        var snapshot = new NotificationSettingsSnapshot(
            severity,
            input.TelegramEnabled,
            token,
            input.TelegramChatId,
            input.EmailEnabled,
            input.EmailHost,
            input.EmailPort > 0 ? input.EmailPort : 587,
            input.EmailUseSsl,
            input.EmailUsername,
            password,
            input.EmailFrom,
            input.EmailTo);

        await store.SaveAsync(snapshot, ct).ConfigureAwait(false);
        return MutationResult.Ok();
    }

    /// <summary>
    /// Sends a test message through one channel ("Telegram" or "Email") using the
    /// currently saved settings, so the user can verify them from the dashboard.
    /// </summary>
    public async Task<MutationResult> SendTestNotificationAsync(string channel, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var senders = scope.ServiceProvider.GetServices<INotificationSender>();
        var sender = senders.FirstOrDefault(x => string.Equals(x.Channel, channel, StringComparison.OrdinalIgnoreCase));

        if (sender is null)
        {
            return MutationResult.Fail($"Unknown channel '{channel}'.");
        }

        if (!sender.IsEnabled)
        {
            return MutationResult.Fail($"{sender.Channel} is not enabled and configured. Save the settings first.");
        }

        var message = new NotificationMessage(
            "Test notification",
            $"This is a PiProxyGuard test message sent at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.",
            NotificationSeverity.Critical);

        var ok = await sender.SendAsync(message, ct).ConfigureAwait(false);
        return ok
            ? MutationResult.Ok()
            : MutationResult.Fail($"{sender.Channel} test failed — check the token/credentials and the server logs.");
    }

    private static string? Mask(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var trimmed = secret.Trim();
        var tail = trimmed.Length <= 4 ? trimmed : trimmed[^4..];
        return $"\u2022\u2022\u2022{tail}";
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
