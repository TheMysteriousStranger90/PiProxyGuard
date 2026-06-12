using Microsoft.Extensions.Options;
using PiProxyGuard.Infrastructure.Detection;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Worker.Services;

/// <summary>
/// Runs the suspicious-activity detector on a fixed interval over the
/// most recent window of ingested log entries.
/// </summary>
public class SuspiciousActivityService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DetectionOptions _options;
    private readonly ILogger<SuspiciousActivityService> _logger;

    public SuspiciousActivityService(
        IServiceScopeFactory scopeFactory,
        IOptions<DetectionOptions> options,
        ILogger<SuspiciousActivityService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Suspicious-activity detector started: every {Interval} min, window {Window} min",
            _options.IntervalMinutes, _options.WindowMinutes);

        // Give the ingestion service a head start on first run.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var detector = scope.ServiceProvider.GetRequiredService<SuspiciousActivityDetector>();
                await detector.AnalyzeAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Detection cycle failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }
}
