using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Infrastructure.Settings;

/// <summary>
/// Periodically refreshes the cached security settings so the synchronous
/// <see cref="ISecuritySettingsStore.Current"/> stays fresh in every process
/// (including the Worker, which reads but never edits the settings). Runs in
/// both the Worker and the API host.
/// </summary>
public sealed class SecuritySettingsRefresher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly ISecuritySettingsStore _store;
    private readonly ILogger<SecuritySettingsRefresher> _logger;

    public SecuritySettingsRefresher(
        ISecuritySettingsStore store,
        ILogger<SecuritySettingsRefresher> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm the cache once at startup, then keep it fresh.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.GetAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Security settings refresh failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
