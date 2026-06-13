using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Statistics;
using PiProxyGuard.Infrastructure.Persistence;
using PiProxyGuard.Infrastructure.Persistence.Repositories;
using Xunit;

namespace PiProxyGuard.Tests;

/// <summary>
/// Exercises the aggregations behind the new stats endpoints, which surface
/// the previously unused log fields (Method, ContentType, ResultCode, ElapsedMs).
/// </summary>
public class ProxyLogRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;
    private readonly ProxyLogRepository _repository;
    private readonly DateTime _from = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly DateTime _to = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    public ProxyLogRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();
        _repository = new ProxyLogRepository(_dbContext);
    }

    [Fact]
    public async Task Method_breakdown_groups_by_http_method()
    {
        Seed(method: "GET", bytes: 100);
        Seed(method: "GET", bytes: 200);
        Seed(method: "CONNECT", bytes: 50);
        await _dbContext.SaveChangesAsync();

        var methods = await _repository.GetMethodBreakdownAsync(_from, _to);

        Assert.Equal(2, methods.Count);
        var get = Assert.Single(methods, m => m.Method == "GET");
        Assert.Equal(2, get.Requests);
        Assert.Equal(300, get.Bytes);
    }

    [Fact]
    public async Task ContentType_breakdown_ignores_null_types()
    {
        Seed(contentType: "text/html");
        Seed(contentType: "text/html");
        Seed(contentType: "image/png");
        Seed(contentType: null);
        await _dbContext.SaveChangesAsync();

        var types = await _repository.GetContentTypeBreakdownAsync(_from, _to, count: 10);

        Assert.Equal(2, types.Count);
        Assert.Equal("text/html", types[0].ContentType);
        Assert.Equal(2, types[0].Requests);
    }

    [Fact]
    public async Task ResultCode_breakdown_groups_by_squid_result()
    {
        Seed(resultCode: "TCP_HIT");
        Seed(resultCode: "TCP_MISS");
        Seed(resultCode: "TCP_MISS");
        await _dbContext.SaveChangesAsync();

        var codes = await _repository.GetResultCodeBreakdownAsync(_from, _to);

        Assert.Equal("TCP_MISS", codes[0].ResultCode);
        Assert.Equal(2, codes[0].Requests);
    }

    [Fact]
    public async Task Slowest_hosts_uses_elapsed_ms_and_respects_min_requests()
    {
        Seed(host: "slow.example.com", elapsedMs: 800);
        Seed(host: "slow.example.com", elapsedMs: 1200);
        Seed(host: "fast.example.com", elapsedMs: 10); // single request, below minRequests
        await _dbContext.SaveChangesAsync();

        var slow = await _repository.GetSlowestHostsAsync(_from, _to, count: 10, minRequests: 2);

        var host = Assert.Single(slow);
        Assert.Equal("slow.example.com", host.Host);
        Assert.Equal(1000, host.AverageElapsedMs);
        Assert.Equal(1200, host.MaxElapsedMs);
    }

    private void Seed(
        string method = "GET", string? contentType = "text/html", string resultCode = "TCP_MISS",
        string host = "example.com", long bytes = 100, int elapsedMs = 100)
    {
        _dbContext.LogEntries.Add(new ProxyLogEntry
        {
            TimestampUtc = _from.AddMinutes(1),
            ElapsedMs = elapsedMs,
            ClientIp = "192.168.1.10",
            ResultCode = resultCode,
            StatusCode = 200,
            Bytes = bytes,
            Method = method,
            Url = $"http://{host}/x",
            Host = host,
            ContentType = contentType,
            WasDenied = false
        });
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
