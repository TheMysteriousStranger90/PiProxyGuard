using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Detection;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Persistence;
using Xunit;

namespace PiProxyGuard.Tests;

public class SuspiciousActivityDetectorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;

    private static readonly DetectionOptions Options = new()
    {
        WindowMinutes = 5,
        MaxRequestsPerWindow = 10,
        MaxBytesPerWindow = 1024 * 1024,
        MaxDeniedPerWindow = 3
    };

    public SuspiciousActivityDetectorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new AppDbContext(dbOptions);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task Raises_alert_for_request_flood()
    {
        var now = DateTime.UtcNow;
        AddEntries("192.168.1.50", now, count: 25, bytesEach: 100);
        await _dbContext.SaveChangesAsync();

        var raised = await CreateDetector().AnalyzeAsync(now, CancellationToken.None);

        Assert.Equal(1, raised);
        var alert = Assert.Single(_dbContext.Alerts);
        Assert.Equal(AlertType.HighRequestRate, alert.Type);
        Assert.Equal("192.168.1.50", alert.ClientIp);
    }

    [Fact]
    public async Task Raises_alert_for_repeated_denied_requests()
    {
        var now = DateTime.UtcNow;
        AddEntries("192.168.1.60", now, count: 5, bytesEach: 100, denied: true);
        await _dbContext.SaveChangesAsync();

        await CreateDetector().AnalyzeAsync(now, CancellationToken.None);

        Assert.Contains(_dbContext.Alerts, a => a.Type == AlertType.RepeatedDeniedRequests);
    }

    [Fact]
    public async Task Raises_alert_for_blocked_domain_contact()
    {
        var now = DateTime.UtcNow;
        _dbContext.BlockedDomains.Add(new BlockedDomain
        {
            Domain = "evil.example.com",
            Source = BlockSource.Feed,
            CreatedAtUtc = now,
            IsActive = true
        });
        AddEntries("192.168.1.70", now, count: 2, bytesEach: 100, host: "evil.example.com");
        await _dbContext.SaveChangesAsync();

        await CreateDetector().AnalyzeAsync(now, CancellationToken.None);

        var alert = Assert.Single(_dbContext.Alerts, a => a.Type == AlertType.BlockedDomainContact);
        Assert.Contains("evil.example.com", alert.Description);
    }

    [Fact]
    public async Task Does_not_duplicate_open_alerts_within_an_hour()
    {
        var now = DateTime.UtcNow;
        AddEntries("192.168.1.80", now, count: 25, bytesEach: 100);
        await _dbContext.SaveChangesAsync();

        var first = await CreateDetector().AnalyzeAsync(now, CancellationToken.None);
        var second = await CreateDetector().AnalyzeAsync(now, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task Quiet_traffic_raises_no_alerts()
    {
        var now = DateTime.UtcNow;
        AddEntries("192.168.1.90", now, count: 3, bytesEach: 100);
        await _dbContext.SaveChangesAsync();

        var raised = await CreateDetector().AnalyzeAsync(now, CancellationToken.None);

        Assert.Equal(0, raised);
        Assert.Empty(_dbContext.Alerts);
    }

    private SuspiciousActivityDetector CreateDetector() =>
        new(new UnitOfWork(_dbContext), Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<SuspiciousActivityDetector>.Instance);

    private void AddEntries(
        string clientIp, DateTime windowEnd, int count, long bytesEach,
        bool denied = false, string host = "ok.example.com")
    {
        for (var i = 0; i < count; i++)
        {
            _dbContext.LogEntries.Add(new ProxyLogEntry
            {
                TimestampUtc = windowEnd.AddSeconds(-10 - i),
                ClientIp = clientIp,
                ResultCode = denied ? "TCP_DENIED" : "TCP_MISS",
                StatusCode = denied ? 403 : 200,
                Bytes = bytesEach,
                Method = "GET",
                Url = $"http://{host}/{i}",
                Host = host,
                WasDenied = denied
            });
        }
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
