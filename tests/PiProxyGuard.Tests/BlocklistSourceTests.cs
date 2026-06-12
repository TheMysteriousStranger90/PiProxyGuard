using PiProxyGuard.Infrastructure.Blocklists;
using Xunit;

namespace PiProxyGuard.Tests;

public class BlocklistSourceTests
{
    [Fact]
    public void Hosts_format_extracts_domains_and_skips_comments()
    {
        const string content = """
            # This is a hosts-style blocklist
            127.0.0.1 localhost
            0.0.0.0 ads.example.com
            0.0.0.0 tracker.example.net # inline comment
            ! adblock-style comment
            0.0.0.0 MALWARE.example.org
            """;

        var source = new PlainTextBlocklistSource("hosts");
        var domains = source.ExtractDomains(content).ToList();

        Assert.Equal(["ads.example.com", "tracker.example.net", "malware.example.org"], domains);
    }

    [Fact]
    public void Plain_format_extracts_one_domain_per_line()
    {
        const string content = """
            bad.example.com
            ; comment
            *.wildcard.example.net
            not a domain
            """;

        var source = new PlainTextBlocklistSource("plain");
        var domains = source.ExtractDomains(content).ToList();

        Assert.Equal(["bad.example.com", "wildcard.example.net"], domains);
    }

    [Fact]
    public void Html_format_extracts_domains_via_css_selector()
    {
        const string html = """
            <html><body>
              <table id="threats">
                <tr><td class="domain">evil.example.com</td><td>malware</td></tr>
                <tr><td class="domain">phish.example.net</td><td>phishing</td></tr>
                <tr><td class="domain">not a domain</td><td>broken row</td></tr>
              </table>
            </body></html>
            """;

        var source = new HtmlBlocklistSource("table#threats td.domain");
        var domains = source.ExtractDomains(html).ToList();

        Assert.Equal(["evil.example.com", "phish.example.net"], domains);
    }
}
