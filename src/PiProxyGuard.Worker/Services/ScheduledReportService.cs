using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Infrastructure.Reports;

namespace PiProxyGuard.Worker.Services;

/// <summary>
/// Builds the traffic-and-security digest once a day at the configured local
/// hour and delivers it through the existing notification channels. Reuses
/// <see cref="DigestReportBuilder"/> (the same report the API exposes on demand)
/// and <see cref="INotificationDispatcher"/>, so it adds scheduling only.
/// Reads its configuration from the runtime <see cref="ISecuritySettingsStore"/>
/// every minute, so it can be enabled/disabled or rescheduled from the dashboard
/// without a restart. Disabled by default.
/// </summary>
public class ScheduledReportService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecuritySettingsStore _settings;
    private readonly ILogger<ScheduledReportService> _logger;

    private DateOnly _lastSentLocalDate = DateOnly.MinValue;

    public ScheduledReportService(
        IServiceScopeFactory scopeFactory,
        ISecuritySettingsStore settings,
        ILogger<ScheduledReportService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled digest service started (polling settings every {Seconds}s)",
            PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled digest cycle failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _settings.GetAsync(cancellationToken);
        if (!snapshot.DailyDigestEnabled)
        {
            return;
        }

        var now = DateTime.Now;
        var hour = Math.Clamp(snapshot.DailyReportHour, 0, 23);
        var today = DateOnly.FromDateTime(now);

        // Fire once, when we first observe the scheduled hour on a new local day.
        if (now.Hour != hour || _lastSentLocalDate == today)
        {
            return;
        }

        _lastSentLocalDate = today;
        await SendDigestAsync(snapshot, cancellationToken);
    }

    private async Task SendDigestAsync(SecuritySettingsSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        if (!dispatcher.HasEnabledChannels)
        {
            _logger.LogInformation("Scheduled digest skipped: no notification channel is enabled");
            return;
        }

        var window = Math.Max(1, snapshot.DigestWindowHours);
        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddHours(-window);

        var builder = scope.ServiceProvider.GetRequiredService<DigestReportBuilder>();
        var report = await builder.BuildAsync(fromUtc, toUtc, snapshot.DigestTitle, cancellationToken);

        var message = new NotificationMessage(
            report.Title,
            report.PlainText,
            snapshot.DigestSeverity);

        var delivered = await dispatcher.DispatchAsync(message, cancellationToken);
        _logger.LogInformation("Scheduled digest delivered to {Count} channel(s)", delivered);
    }
}
