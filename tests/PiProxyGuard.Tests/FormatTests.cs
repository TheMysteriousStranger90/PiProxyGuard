using PiProxyGuard.Web.Services;
using Xunit;

namespace PiProxyGuard.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void Bytes_formats_human_readable(long input, string expected) =>
        Assert.Equal(expected, Format.Bytes(input));

    [Theory]
    [InlineData(5, "5")]
    [InlineData(12345, "12,345")]
    [InlineData(1000000, "1,000,000")]
    public void Number_groups_thousands(long input, string expected) =>
        Assert.Equal(expected, Format.Number(input));

    [Fact]
    public void Ago_uses_compact_buckets()
    {
        var now = DateTime.UtcNow;
        Assert.Equal("just now", Format.Ago(now));
        Assert.Equal("5 min ago", Format.Ago(now.AddMinutes(-5)));
        Assert.Equal("3 h ago", Format.Ago(now.AddHours(-3)));
        Assert.Equal("2 d ago", Format.Ago(now.AddDays(-2)));
    }

    [Fact]
    public void Ago_never_returns_negative_for_future_timestamps() =>
        Assert.Equal("just now", Format.Ago(DateTime.UtcNow.AddMinutes(5)));

    [Fact]
    public void MutationResult_helpers_set_flags()
    {
        Assert.True(MutationResult.Ok().Success);
        Assert.Null(MutationResult.Ok().Error);
        Assert.False(MutationResult.Fail("nope").Success);
        Assert.Equal("nope", MutationResult.Fail("nope").Error);
    }
}
