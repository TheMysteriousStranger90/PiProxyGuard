using AngleSharp.Html.Parser;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;

namespace PiProxyGuard.Infrastructure.Blocklists;

/// <summary>
/// Extracts domains from an HTML page using an AngleSharp CSS selector,
/// the same way you would in the browser: document.QuerySelectorAll(".domain").
/// Useful for threat feeds that only publish an HTML table of bad domains.
/// </summary>
public class HtmlBlocklistSource : IBlocklistSource
{
    private readonly string _cssSelector;
    private readonly HtmlParser _parser = new();

    public HtmlBlocklistSource(string cssSelector) => _cssSelector = cssSelector;

    public string Format => "html";

    public IEnumerable<string> ExtractDomains(string content)
    {
        var document = _parser.ParseDocument(content);

        foreach (var element in document.QuerySelectorAll(_cssSelector))
        {
            if (DomainUtils.NormalizeDomain(element.TextContent) is { } domain)
            {
                yield return domain;
            }
        }
    }
}
