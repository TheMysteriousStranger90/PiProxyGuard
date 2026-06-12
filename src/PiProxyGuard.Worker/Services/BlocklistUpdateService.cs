using Microsoft.Extensions.Options;
using PiProxyGuard.Infrastructure.Blocklists;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Worker.Services;

/// <summary>
/// Periodically downloads fresh blocklist feeds and updates the Squid ACL.
/// Runs once immediately on startup, then on the configured interval.
/// </summary>
public class BlocklistUpdateService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BlocklistOptions _options;
    private readonly ILogger<BlocklistUpdateService> _logger;

    public BlocklistUpdateService(
        IServiceScopeFactory scopeFactory,
        IOptions<BlocklistOptions> options,
        ILogger<BlocklistUpdateService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Blocklist updater started: {FeedCount} feeds, every {Hours} h",
            _options.Feeds.Count, _options.UpdateIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var updater = scope.ServiceProvider.GetRequiredService<BlocklistUpdater>();
                await updater.UpdateAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blocklist update cycle failed");
            }

            await Task.Delay(TimeSpan.FromHours(_options.UpdateIntervalHours), stoppingToken);
        }
    }
}
