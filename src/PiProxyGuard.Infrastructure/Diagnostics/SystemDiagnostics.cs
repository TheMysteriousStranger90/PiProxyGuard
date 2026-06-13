using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Persistence;

namespace PiProxyGuard.Infrastructure.Diagnostics;

/// <summary>Status of one diagnostic check.</summary>
public enum DiagnosticStatus
{
    Ok = 0,
    Warning = 1,
    Error = 2
}

/// <summary>A single named check result.</summary>
public sealed record DiagnosticCheck(string Name, DiagnosticStatus Status, string Detail);

/// <summary>The overall self-diagnostics report.</summary>
public sealed record DiagnosticsReport(DiagnosticStatus Status, DateTime GeneratedAtUtc, IReadOnlyList<DiagnosticCheck> Checks)
{
    /// <summary>True when nothing is in the Error state.</summary>
    public bool IsHealthy => Status != DiagnosticStatus.Error;
}

/// <summary>
/// Runs a set of lightweight self-checks (database reachable, log ingestion
/// fresh, ACL file present, blocklist non-empty) and rolls them up into a
/// single report. Backs the API health/readiness endpoint.
/// </summary>
public class SystemDiagnostics
{
    private readonly AppDbContext _dbContext;
    private readonly BlocklistOptions _blocklist;
    private readonly AccessLogOptions _accessLog;

    public SystemDiagnostics(
        AppDbContext dbContext,
        IOptions<BlocklistOptions> blocklist,
        IOptions<AccessLogOptions> accessLog)
    {
        _dbContext = dbContext;
        _blocklist = blocklist.Value;
        _accessLog = accessLog.Value;
    }

    public async Task<DiagnosticsReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var checks = new List<DiagnosticCheck>
        {
            await CheckDatabaseAsync(cancellationToken),
            await CheckIngestionAsync(now, cancellationToken),
            await CheckBlocklistAsync(cancellationToken),
            CheckAclFile()
        };

        var status = checks.Max(c => c.Status);
        return new DiagnosticsReport(status, now, checks);
    }

    private async Task<DiagnosticCheck> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? new DiagnosticCheck("database", DiagnosticStatus.Ok, "Reachable")
                : new DiagnosticCheck("database", DiagnosticStatus.Error, "Cannot connect");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiagnosticCheck("database", DiagnosticStatus.Error, ex.Message);
        }
    }

    private async Task<DiagnosticCheck> CheckIngestionAsync(DateTime now, CancellationToken cancellationToken)
    {
        var state = await _dbContext.IngestionStates
            .OrderByDescending(s => s.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return new DiagnosticCheck("log-ingestion", DiagnosticStatus.Warning, "No ingestion has run yet");
        }

        var age = now - state.UpdatedAtUtc;
        // Two missed poll intervals is a warning; ten is an error.
        var poll = TimeSpan.FromSeconds(Math.Max(_accessLog.PollIntervalSeconds, 1));
        var status = age > poll * 10 ? DiagnosticStatus.Error
            : age > poll * 4 ? DiagnosticStatus.Warning
            : DiagnosticStatus.Ok;

        return new DiagnosticCheck("log-ingestion", status,
            $"Last update {age.TotalSeconds:F0}s ago at offset {state.Offset}");
    }

    private async Task<DiagnosticCheck> CheckBlocklistAsync(CancellationToken cancellationToken)
    {
        var active = await _dbContext.BlockedDomains.CountAsync(d => d.IsActive, cancellationToken);
        return active > 0
            ? new DiagnosticCheck("blocklist", DiagnosticStatus.Ok, $"{active} active domains")
            : new DiagnosticCheck("blocklist", DiagnosticStatus.Warning, "Blocklist is empty");
    }

    private DiagnosticCheck CheckAclFile()
    {
        try
        {
            if (!File.Exists(_blocklist.AclFilePath))
            {
                return new DiagnosticCheck("acl-file", DiagnosticStatus.Warning,
                    $"Not written yet: {_blocklist.AclFilePath}");
            }

            var info = new FileInfo(_blocklist.AclFilePath);
            return new DiagnosticCheck("acl-file", DiagnosticStatus.Ok,
                $"{info.Length} bytes, modified {info.LastWriteTimeUtc:u}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DiagnosticCheck("acl-file", DiagnosticStatus.Error, ex.Message);
        }
    }

    /// <summary>Counts open alerts grouped by type — used by the digest report.</summary>
    public async Task<IReadOnlyDictionary<AlertType, int>> GetOpenAlertCountsAsync(CancellationToken cancellationToken = default)
    {
        var grouped = await _dbContext.Alerts
            .Where(a => !a.IsAcknowledged)
            .GroupBy(a => a.Type)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return grouped.ToDictionary(x => x.Key, x => x.Count);
    }
}
