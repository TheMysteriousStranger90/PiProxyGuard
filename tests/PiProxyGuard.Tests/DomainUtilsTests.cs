using PiProxyGuard.Domain.Common;
using Xunit;

namespace PiProxyGuard.Tests;

public class DomainUtilsTests
{
    [Theory]
    [InlineData("http://example.com/page.html", "example.com")]
    [InlineData("https://Sub.Example.COM:8443/x?q=1", "sub.example.com")]
    [InlineData("www.youtube.com:443", "www.youtube.com")]
    [InlineData("example.com", "example.com")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void ExtractHost_handles_common_url_shapes(string url, string expected)
    {
        Assert.Equal(expected, DomainUtils.ExtractHost(url));
    }

    [Theory]
    [InlineData("Ads.Example.com", "ads.example.com")]
    [InlineData("*.tracker.net", "tracker.net")]
    [InlineData("example.org.", "example.org")]
    public void NormalizeDomain_normalizes_valid_domains(string raw, string expected)
    {
        Assert.Equal(expected, DomainUtils.NormalizeDomain(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("noperiods")]
    [InlineData("bad domain.com")]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    public void NormalizeDomain_rejects_invalid_values(string? raw)
    {
        Assert.Null(DomainUtils.NormalizeDomain(raw));
    }
}
