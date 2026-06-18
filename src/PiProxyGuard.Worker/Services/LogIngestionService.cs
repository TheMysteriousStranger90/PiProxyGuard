using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Parsing;

namespace PiProxyGuard.Worker.Services;

/// <summary>
/// Tails the Squid access log, parses new lines and stores them in SQLite.
/// Resumes from the last byte offset after restarts and survives log rotation.
/// Persistence goes through <see cref="IUnitOfWork"/>.
/// </summary>
public class LogIngestionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProxyLogParser _parser;
    private readonly AccessLogOptions _options;
    private readonly ILogger<LogIngestionService> _logger;

    public LogIngestionService(
        IServiceScopeFactory scopeFactory,
        IProxyLogParser parser,
        IOptions<AccessLogOptions> options,
        ILogger<LogIngestionService> logger)
    {
        _scopeFactory = scopeFactory;
        _parser = parser;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Log ingestion started for {Path}", _options.Path);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IngestOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Log ingestion cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }
    }

    private async Task IngestOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var state = await unitOfWork.IngestionStates.GetByFilePathAsync(_options.Path, cancellationToken)
            ?? new LogIngestionState { FilePath = _options.Path };

        var result = LogFileTailer.ReadNewLines(_options.Path, state.Offset, state.FirstLineFingerprint);

        var parsed = 0;
        var batch = new List<ProxyLogEntry>(_options.BatchSize);

        foreach (var line in result.Lines)
        {
            if (_parser.TryParse(line, out var entry))
            {
                batch.Add(entry!);
                parsed++;

                if (batch.Count >= _options.BatchSize)
                {
                    unitOfWork.ProxyLogs.AddRange(batch);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            unitOfWork.ProxyLogs.AddRange(batch);
        }

        // Always advance the heartbeat so the diagnostics freshness check reflects
        // that the poller is alive and keeping up. An idle proxy (no new requests)
        // must not be reported as an ingestion failure / readiness 503.
        state.Offset = result.NewOffset;
        state.FirstLineFingerprint = result.FirstLineFingerprint;
        state.UpdatedAtUtc = DateTime.UtcNow;

        if (state.Id == 0)
        {
            unitOfWork.IngestionStates.Add(state);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (parsed > 0)
        {
            await CleanupOldEntriesAsync(unitOfWork, cancellationToken);
            _logger.LogInformation("Ingested {Count} log entries (offset {Offset})", parsed, state.Offset);
        }
    }

    private async Task CleanupOldEntriesAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if (_options.RetentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        await unitOfWork.ProxyLogs.DeleteOlderThanAsync(cutoff, cancellationToken);
    }
}
