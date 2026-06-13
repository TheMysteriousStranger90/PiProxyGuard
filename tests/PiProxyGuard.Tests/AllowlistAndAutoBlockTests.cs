using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Detection;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Persistence;
using PiProxyGuard.Infrastructure.Persistence.Repositories;
using Xunit;

namespace PiProxyGuard.Tests;

public class AllowlistAndAutoBlockTests : IDisposable
{
    private const string DgaHost = "kq3v9z7xj2m8p1ld.com";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;

    public AllowlistAndAutoBlockTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task Allowlist_repository_round_trips()
    {
        var repo = new AllowedDomainRepository(_dbContext);
        repo.Add(new AllowedDomain { Domain = "trusted.example.com", CreatedAtUtc = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        Assert.NotNull(await repo.FindByDomainAsync("trusted.example.com"));
        Assert.Contains("trusted.example.com", await repo.GetAllDomainNamesAsync());
        Assert.Single(await repo.SearchAsync("trusted", 10));
    }

    [Fact]
    public async Task Expired_auto_blocks_are_returned_for_sweeping()
    {
        var repo = new BlockedDomainRepository(_dbContext);
        var now = DateTime.UtcNow;
        repo.Add(new BlockedDomain
        {
            Domain = "temp.example.com", Source = BlockSource.Auto, IsActive = true,
            CreatedAtUtc = now.AddHours(-2), ExpiresAtUtc = now.AddHours(-1)
        });
        repo.Add(new BlockedDomain
        {
            Domain = "still.example.com", Source = BlockSource.Auto, IsActive = true,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1)
        });
        repo.Add(new BlockedDomain
        {
            Domain = "manual.example.com", Source = BlockSource.Manual, IsActive = true,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(-1) // manual never expires
        });
        await _dbContext.SaveChangesAsync();

        var expired = await repo.GetExpiredAutoBlocksAsync(now);

        Assert.Single(expired);
        Assert.Equal("temp.example.com", expired[0].Domain);
    }

    [Fact]
    public async Task Detector_flags_and_auto_blocks_dga_domain_with_ttl()
    {
        AddContacts("192.168.1.40", DgaHost, count: 3);
        await _dbContext.SaveChangesAsync();

        var raised = await CreateDetector(autoBlock: true).AnalyzeAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.True(raised >= 1);
        Assert.Contains(_dbContext.Alerts, a => a.Type == AlertType.SuspiciousDomain);

        var blocked = Assert.Single(_dbContext.BlockedDomains, d => d.Domain == DgaHost);
        Assert.Equal(BlockSource.Auto, blocked.Source);
        Assert.NotNull(blocked.ExpiresAtUtc);
    }

    [Fact]
    public async Task Allowlisted_dga_domain_is_not_flagged()
    {
        _dbContext.AllowedDomains.Add(new AllowedDomain { Domain = DgaHost, CreatedAtUtc = DateTime.UtcNow });
        AddContacts("192.168.1.41", DgaHost, count: 3);
        await _dbContext.SaveChangesAsync();

        await CreateDetector(autoBlock: true).AnalyzeAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.DoesNotContain(_dbContext.Alerts, a => a.Type == AlertType.SuspiciousDomain);
        Assert.DoesNotContain(_dbContext.BlockedDomains, d => d.Domain == DgaHost);
    }

    private SuspiciousActivityDetector CreateDetector(bool autoBlock)
    {
        var options = new DetectionOptions
        {
            WindowMinutes = 5,
            MaxRequestsPerWindow = 1000,
            MaxBytesPerWindow = 1024L * 1024 * 1024,
            MaxDeniedPerWindow = 100,
            DetectTrafficSpikes = false,
            DetectSuspiciousDomains = true,
            AutoBlockSuspiciousHosts = autoBlock,
            AutoBlockTtlHours = 24
        };
        return new SuspiciousActivityDetector(
            new UnitOfWork(_dbContext), Options.Create(options),
            new ClientProfileResolver(options), new NoopNotificationDispatcher(),
            NullLogger<SuspiciousActivityDetector>.Instance);
    }

    private void AddContacts(string clientIp, string host, int count)
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < count; i++)
        {
            _dbContext.LogEntries.Add(new ProxyLogEntry
            {
                TimestampUtc = now.AddSeconds(-10 - i),
                ClientIp = clientIp,
                ResultCode = "TCP_MISS",
                StatusCode = 200,
                Bytes = 100,
                Method = "GET",
                Url = $"http://{host}/{i}",
                Host = host,
                WasDenied = false
            });
        }
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
