using PiProxyGuard.Infrastructure.Parsing;
using Xunit;

namespace PiProxyGuard.Tests;

public class SquidAccessLogParserTests
{
    private readonly SquidAccessLogParser _parser = new();

    [Fact]
    public void Parses_regular_get_request()
    {
        const string line =
            "1718100000.123    245 192.168.1.10 TCP_MISS/200 51234 GET http://example.com/page.html - HIER_DIRECT/93.184.216.34 text/html";

        var ok = _parser.TryParse(line, out var entry);

        Assert.True(ok);
        Assert.NotNull(entry);
        Assert.Equal("192.168.1.10", entry!.ClientIp);
        Assert.Equal("TCP_MISS", entry.ResultCode);
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal(51234, entry.Bytes);
        Assert.Equal("GET", entry.Method);
        Assert.Equal("example.com", entry.Host);
        Assert.Equal("text/html", entry.ContentType);
        Assert.Equal(245, entry.ElapsedMs);
        Assert.False(entry.WasDenied);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1718100000.123), entry.TimestampUtc, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Parses_connect_request_with_host_port_target()
    {
        const string line =
            "1718100100.456   1200 192.168.1.20 TCP_TUNNEL/200 998877 CONNECT www.youtube.com:443 - HIER_DIRECT/142.250.74.78 -";

        var ok = _parser.TryParse(line, out var entry);

        Assert.True(ok);
        Assert.Equal("www.youtube.com", entry!.Host);
        Assert.Equal("CONNECT", entry.Method);
        Assert.Null(entry.ContentType);
    }

    [Fact]
    public void Marks_denied_requests()
    {
        const string line =
            "1718100200.789      0 192.168.1.30 TCP_DENIED/403 3821 GET http://ads.tracker.example/banner.js - HIER_NONE/- text/html";

        var ok = _parser.TryParse(line, out var entry);

        Assert.True(ok);
        Assert.True(entry!.WasDenied);
        Assert.Equal("TCP_DENIED", entry.ResultCode);
        Assert.Equal(403, entry.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# comment line")]
    [InlineData("not a log line at all")]
    [InlineData("garbage 123 only four fields")]
    public void Rejects_invalid_lines(string line)
    {
        var ok = _parser.TryParse(line, out var entry);

        Assert.False(ok);
        Assert.Null(entry);
    }
}
