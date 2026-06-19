using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Infrastructure.Notifications;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Squid;
using Xunit;

namespace PiProxyGuard.Tests;

public class NotificationDispatcherTests
{
    private sealed class FakeSender : INotificationSender
    {
        private readonly bool _ok;
        public FakeSender(string channel, bool enabled, bool ok = true)
        {
            Channel = channel;
            IsEnabled = enabled;
            _ok = ok;
        }

        public string Channel { get; }
        public bool IsEnabled { get; }
        public int SentCount { get; private set; }

        public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            SentCount++;
            return Task.FromResult(_ok);
        }
    }

    private sealed class FakeSettingsStore : INotificationSettingsStore
    {
        private readonly NotificationSettingsSnapshot _snapshot;
        public FakeSettingsStore(NotificationSeverity min) =>
            _snapshot = new NotificationSettingsSnapshot(
                min, false, null, null, false, null, 587, true, null, null, null, null);

        public NotificationSettingsSnapshot Current => _snapshot;
        public Task<NotificationSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);
        public Task SaveAsync(NotificationSettingsSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static NotificationDispatcher Build(IEnumerable<INotificationSender> senders, NotificationSeverity min = NotificationSeverity.Warning) =>
        new(senders, new FakeSettingsStore(min), NullLogger<NotificationDispatcher>.Instance);

    [Fact]
    public async Task Fans_out_only_to_enabled_channels()
    {
        var a = new FakeSender("a", enabled: true);
        var b = new FakeSender("b", enabled: false);
        var dispatcher = Build([a, b]);

        Assert.True(dispatcher.HasEnabledChannels);
        var delivered = await dispatcher.DispatchAsync(
            new NotificationMessage("t", "body", NotificationSeverity.Critical), CancellationToken.None);

        Assert.Equal(1, delivered);
        Assert.Equal(1, a.SentCount);
        Assert.Equal(0, b.SentCount);
    }

    [Fact]
    public async Task Suppresses_messages_below_minimum_severity()
    {
        var a = new FakeSender("a", enabled: true);
        var dispatcher = Build([a], min: NotificationSeverity.Critical);

        var delivered = await dispatcher.DispatchAsync(
            new NotificationMessage("t", "body", NotificationSeverity.Warning), CancellationToken.None);

        Assert.Equal(0, delivered);
        Assert.Equal(0, a.SentCount);
    }

    [Fact]
    public void No_enabled_channels_reports_false()
    {
        var dispatcher = Build([new FakeSender("a", enabled: false)]);
        Assert.False(dispatcher.HasEnabledChannels);
    }
}

public class SquidAclWriterTests
{
    /// <summary>Test writer that can be told whether the reload "succeeds".</summary>
    private sealed class TestAclWriter : SquidAclWriter
    {
        private readonly bool _reloadOk;
        public int ReloadCalls { get; private set; }

        public TestAclWriter(BlocklistOptions options, bool reloadOk)
            : base(Options.Create(options), NullLogger<SquidAclWriter>.Instance)
        {
            _reloadOk = reloadOk;
        }

        protected override Task<bool> RunReloadCommandAsync(CancellationToken cancellationToken)
        {
            ReloadCalls++;
            return Task.FromResult(_reloadOk);
        }
    }

    private static BlocklistOptions OptionsFor(string aclPath) => new()
    {
        AclFilePath = aclPath,
        ReloadCommand = "true",
        BackupAclBeforeWrite = true
    };

    [Fact]
    public async Task Writes_acl_and_reports_success_on_good_reload()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "blocked.acl");
            var writer = new TestAclWriter(OptionsFor(path), reloadOk: true);

            var rewritten = await writer.WriteIfChangedAsync(["evil.example.com"], CancellationToken.None);

            Assert.True(rewritten);
            Assert.True(writer.LastReloadSucceeded);
            Assert.Contains(".evil.example.com", await File.ReadAllTextAsync(path));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Rolls_back_to_previous_acl_when_reload_fails()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "blocked.acl");

            // First write succeeds and establishes a good baseline ACL.
            var good = new TestAclWriter(OptionsFor(path), reloadOk: true);
            await good.WriteIfChangedAsync(["first.example.com"], CancellationToken.None);
            var baseline = await File.ReadAllTextAsync(path);

            // Second write fails to reload — the file must be restored to the baseline.
            var bad = new TestAclWriter(OptionsFor(path), reloadOk: false);
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
}
