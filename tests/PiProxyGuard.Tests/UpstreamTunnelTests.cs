using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Persistence;
using PiProxyGuard.Infrastructure.Persistence.Repositories;
using PiProxyGuard.Infrastructure.Squid;
using Xunit;

namespace PiProxyGuard.Tests;

public class UpstreamTunnelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;

    public UpstreamTunnelTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task Tunnel_repository_round_trips()
    {
        var repo = new TunneledDomainRepository(_dbContext);
        repo.Add(new TunneledDomain { Domain = "example.com", Reason = "vpn", CreatedAtUtc = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        Assert.NotNull(await repo.FindByDomainAsync("example.com"));
        Assert.Contains("example.com", await repo.GetAllDomainNamesAsync());
        Assert.Single(await repo.SearchAsync("exam", 10));
    }

    /// <summary>Test writer that can be told whether the reload "succeeds".</summary>
    private sealed class TestTunnelAclWriter : TunnelAclWriter
    {
        private readonly bool _reloadOk;
        public int ReloadCalls { get; private set; }

        public TestTunnelAclWriter(TunnelOptions options, bool reloadOk)
            : base(Options.Create(options), NullLogger<TunnelAclWriter>.Instance)
        {
            _reloadOk = reloadOk;
        }

        protected override Task<bool> RunReloadCommandAsync(CancellationToken cancellationToken)
        {
            ReloadCalls++;
            return Task.FromResult(_reloadOk);
        }
    }

    private static TunnelOptions OptionsFor(string aclPath) => new()
    {
        AclFilePath = aclPath,
        ReloadCommand = "true"
    };

    [Fact]
    public async Task Writes_tunnel_acl_with_leading_dot_on_good_reload()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "tunnel_domains.acl");
            var writer = new TestTunnelAclWriter(OptionsFor(path), reloadOk: true);

            var rewritten = await writer.WriteIfChangedAsync(["example.com"], CancellationToken.None);

            Assert.True(rewritten);
            Assert.True(writer.LastReloadSucceeded);
            Assert.Contains(".example.com", await File.ReadAllTextAsync(path));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Rolls_back_tunnel_acl_when_reload_fails()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "tunnel_domains.acl");

            var good = new TestTunnelAclWriter(OptionsFor(path), reloadOk: true);
            await good.WriteIfChangedAsync(["first.example.com"], CancellationToken.None);
            var baseline = await File.ReadAllTextAsync(path);

            var bad = new TestTunnelAclWriter(OptionsFor(path), reloadOk: false);
            var rewritten = await bad.WriteIfChangedAsync(["broken.example.com"], CancellationToken.None);

            Assert.True(rewritten);
            Assert.False(bad.LastReloadSucceeded);
            var current = await File.ReadAllTextAsync(path);
            Assert.Equal(baseline, current);
            Assert.DoesNotContain("broken.example.com", current);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
