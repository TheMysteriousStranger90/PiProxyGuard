using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Parsing;
using PiProxyGuard.Infrastructure.Persistence;

namespace PiProxyGuard.Worker.Services;

/// <summary>
/// Tails the Squid access log, parses new lines and stores them in SQLite.
/// Resumes from the last byte offset after restarts and survives log rotation.
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
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var state = dbContext.IngestionStates.FirstOrDefault(s => s.FilePath == _options.Path)
            ?? new LogIngestionState { FilePath = _options.Path };

        var result = LogFileTailer.ReadNewLines(_options.Path, state.Offset, state.FirstLineFingerprint);
        if (result.Lines.Count == 0 && result.NewOffset == state.Offset)
        {
            return;
        }

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
                    dbContext.LogEntries.AddRange(batch);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            dbContext.LogEntries.AddRange(batch);
        }

        state.Offset = result.NewOffset;
        state.FirstLineFingerprint = result.FirstLineFingerprint;
        state.UpdatedAtUtc = DateTime.UtcNow;

        if (state.Id == 0)
        {
            dbContext.IngestionStates.Add(state);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await CleanupOldEntriesAsync(dbContext, cancellationToken);

        if (parsed > 0)
        {
            _logger.LogInformation("Ingested {Count} log entries (offset {Offset})", parsed, state.Offset);
        }
    }

    private async Task CleanupOldEntriesAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (_options.RetentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        await dbContext.LogEntries
            .Where(e => e.TimestampUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
