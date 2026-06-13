using PiProxyGuard.Domain.Categorization;
using PiProxyGuard.Infrastructure.Detection;
using PiProxyGuard.Infrastructure.Options;
using Xunit;

namespace PiProxyGuard.Tests;

public class DomainEntropyTests
{
    [Theory]
    [InlineData("kq3v9z7xj2m8p1ld.com")] // random 16-char label
    [InlineData("xn7g4kd92ovqp3wz.net")]
    public void Flags_algorithmically_generated_domains(string host)
    {
        Assert.True(DomainEntropy.LooksAlgorithmic(host, 3.5, 12));
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("cdn.cloudflare.com")]
    [InlineData("raspberrypi.org")]
    [InlineData("short.io")]
    public void Does_not_flag_normal_domains(string host)
    {
        Assert.False(DomainEntropy.LooksAlgorithmic(host, 3.5, 12));
    }

    [Fact]
    public void Significant_label_takes_second_level_domain()
    {
        Assert.Equal("example", DomainEntropy.SignificantLabel("www.example.com"));
    }

    [Fact]
    public void Entropy_of_uniform_string_is_zero()
    {
        Assert.Equal(0, DomainEntropy.ShannonEntropy("aaaaaa"), 3);
    }
}

public class RuleBasedDomainCategorizerTests
{
    private readonly RuleBasedDomainCategorizer _sut = new();

    [Theory]
    [InlineData("ads.doubleclick.net", DomainCategory.Advertising)]
    [InlineData("www.google-analytics.com", DomainCategory.Tracking)]
    [InlineData("graph.facebook.com", DomainCategory.Social)]
    public void Categorizes_known_suffixes(string host, DomainCategory expected)
    {
        Assert.Equal(expected, _sut.Categorize(host));
    }

    [Fact]
    public void Unknown_host_is_unknown()
    {
        Assert.Equal(DomainCategory.Unknown, _sut.Categorize("some-random-blog.example"));
    }
}

public class ClientProfileResolverTests
{
    private static DetectionOptions BaseOptions() => new()
    {
        MaxRequestsPerWindow = 600,
        MaxBytesPerWindow = 500L * 1024 * 1024,
        MaxDeniedPerWindow = 20,
        ClientProfiles =
        [
            new ClientProfileOptions { Match = "192.168.1.10", Name = "kid-tablet", Strictness = 0.5 },
            new ClientProfileOptions { Match = "10.0.0.0/8", Name = "guest", MaxRequestsPerWindow = 100 }
        ]
    };

    [Fact]
    public void Exact_ip_match_scales_by_strictness()
    {
        var t = new ClientProfileResolver(BaseOptions()).Resolve("192.168.1.10");
        Assert.Equal("kid-tablet", t.ProfileName);
        Assert.Equal(300, t.MaxRequestsPerWindow); // 600 * 0.5
    }

    [Fact]
    public void Cidr_match_uses_explicit_override()
    {
        var t = new ClientProfileResolver(BaseOptions()).Resolve("10.5.6.7");
        Assert.Equal("guest", t.ProfileName);
        Assert.Equal(100, t.MaxRequestsPerWindow);
    }

    [Fact]
    public void Unmatched_ip_falls_back_to_global_defaults()
    {
        var t = new ClientProfileResolver(BaseOptions()).Resolve("172.16.0.1");
        Assert.Null(t.ProfileName);
        Assert.Equal(600, t.MaxRequestsPerWindow);
    }
}
