using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PiProxyGuard.Web.Services;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure;
using PiProxyGuard.Infrastructure.Persistence;
using Xunit;

namespace PiProxyGuard.Tests;

/// <summary>
/// Exercises the read paths of <see cref="DashboardService"/> through a real DI
/// container and a shared in-memory SQLite database, proving the scope-per-call
/// facade resolves repositories and returns the data the UI renders. Write paths
/// are covered by the repository tests because they also rewrite the Squid ACL.
/// </summary>
public sealed class DashboardServiceTests : IDisposable
{
    private readonly string _connectionString = $"DataSource=file:dashtest-{Guid.NewGuid():N}?mode=memory&cache=shared";

    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;

    public DashboardServiceTests()
    {
        // Keep one connection open so the shared in-memory database survives
        // between the per-scope connections the service opens.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPiProxyGuardInfrastructure(config);
        services.AddSingleton<DashboardService>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        Seed(db);
        db.SaveChanges();
    }

    private static void Seed(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        db.BlockedDomains.Add(new BlockedDomain
        {
            Domain = "ads.example.com", Source = BlockSource.Manual, Reason = "ads",
            IsActive = true, CreatedAtUtc = now
        });
        db.BlockedDomains.Add(new BlockedDomain
        {
            Domain = "tracker.example.com", Source = BlockSource.Feed,
            IsActive = true, CreatedAtUtc = now
        });
        db.AllowedDomains.Add(new AllowedDomain { Domain = "trusted.example.com", CreatedAtUtc = now });
        db.Alerts.Add(new SuspiciousActivityAlert
        {
            ClientIp = "192.168.1.20", Type = AlertType.RepeatedDeniedRequests, Description = "many hosts",
            WindowStartUtc = now.AddMinutes(-5), WindowEndUtc = now, DetectedAtUtc = now,
            IsAcknowledged = false
        });
        db.Alerts.Add(new SuspiciousActivityAlert
        {
            ClientIp = "192.168.1.21", Type = AlertType.RepeatedDeniedRequests, Description = "old",
            WindowStartUtc = now.AddHours(-2), WindowEndUtc = now.AddHours(-2), DetectedAtUtc = now.AddHours(-2),
            IsAcknowledged = true
        });
        db.LogEntries.Add(new ProxyLogEntry
        {
            TimestampUtc = now, ClientIp = "192.168.1.20", Host = "ads.example.com",
            ResultCode = "TCP_DENIED", StatusCode = 403, Bytes = 0, Method = "GET",
            Url = "http://ads.example.com/", WasDenied = true
        });
        db.LogEntries.Add(new ProxyLogEntry
        {
            TimestampUtc = now, ClientIp = "192.168.1.20", Host = "cdn.example.com",
            ResultCode = "TCP_MISS", StatusCode = 200, Bytes = 2048, Method = "GET",
            Url = "http://cdn.example.com/x", WasDenied = false
        });
    }

    private DashboardService Service => _provider.GetRequiredService<DashboardService>();

    [Fact]
    public async Task GetBlockedAsync_returns_all_sources_then_filters()
    {
        var all = await Service.GetBlockedAsync(source: null, search: null, count: 50);
        Assert.Equal(2, all.Count);

        var manualOnly = await Service.GetBlockedAsync(source: "Manual", search: null, count: 50);
        Assert.Single(manualOnly);
        Assert.Equal("ads.example.com", manualOnly[0].Domain);

        var searched = await Service.GetBlockedAsync(source: null, search: "tracker", count: 50);
        Assert.Single(searched);
        Assert.Equal("Feed", searched[0].Source);
    }

    [Fact]
    public async Task GetAllowedAsync_returns_entries()
    {
        var allowed = await Service.GetAllowedAsync(search: null, count: 50);
        Assert.Single(allowed);
        Assert.Equal("trusted.example.com", allowed[0].Domain);
    }

    [Fact]
    public async Task GetAlertsAsync_can_filter_open_only()
    {
        var open = await Service.GetAlertsAsync(onlyUnacknowledged: true, count: 50);
        Assert.Single(open);
        Assert.False(open[0].IsAcknowledged);

        var every = await Service.GetAlertsAsync(onlyUnacknowledged: false, count: 50);
        Assert.Equal(2, every.Count);
    }

    [Fact]
    public async Task GetLatestAlertIdAsync_returns_the_max_id()
    {
        var latest = await Service.GetLatestAlertIdAsync();
        Assert.True(latest > 0);
    }

    [Fact]
    public async Task GetSummaryAsync_aggregates_requests_and_denials()
    {
        var to = DateTime.UtcNow.AddMinutes(1);
        var from = to.AddHours(-1);
        var summary = await Service.GetSummaryAsync(from, to);
        Assert.Equal(2, summary.TotalRequests);
        Assert.Equal(1, summary.DeniedRequests);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }
}
