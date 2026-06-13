using System.Globalization;
using System.Text;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Categorization;

namespace PiProxyGuard.Infrastructure.Reports;

/// <summary>A rendered digest report for a period.</summary>
public sealed record DigestReport(
    string Title,
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime GeneratedAtUtc,
    string PlainText);

/// <summary>
/// Builds a human-readable traffic-and-security digest for a time window:
/// totals, busiest devices and hosts, a category breakdown and the open
/// alerts. Pure aggregation over the repositories — no scheduling, so it can
/// back an on-demand API endpoint or a scheduled e-mail equally well.
/// </summary>
public class DigestReportBuilder
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainCategorizer _categorizer;

    public DigestReportBuilder(IUnitOfWork unitOfWork, IDomainCategorizer categorizer)
    {
        _unitOfWork = unitOfWork;
        _categorizer = categorizer;
    }

    public async Task<DigestReport> BuildAsync(
        DateTime fromUtc, DateTime toUtc, string title, CancellationToken cancellationToken = default)
    {
        var summary = await _unitOfWork.ProxyLogs.GetTrafficSummaryAsync(fromUtc, toUtc, cancellationToken);
        var topClients = await _unitOfWork.ProxyLogs.GetTopClientsAsync(fromUtc, toUtc, 5, cancellationToken);
        var topHosts = await _unitOfWork.ProxyLogs.GetTopHostsAsync(fromUtc, toUtc, 200, cancellationToken);
        var alerts = await _unitOfWork.Alerts.GetAlertsAsync(onlyUnacknowledged: true, 200, cancellationToken);

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(title).Append(' ').Append('(').Append(fromUtc.ToString("u", ci))
            .Append(" — ").Append(toUtc.ToString("u", ci)).Append(')').Append('\n').Append('\n');

        sb.Append("Traffic\n");
        sb.Append("  Requests:  ").Append(summary.TotalRequests.ToString("N0", ci)).Append('\n');
        sb.Append("  Transfer:  ").Append((summary.TotalBytes / (1024.0 * 1024)).ToString("N1", ci)).Append(" MB\n");
        sb.Append("  Denied:    ").Append(summary.DeniedRequests.ToString("N0", ci)).Append('\n');
        sb.Append("  Clients:   ").Append(summary.UniqueClients.ToString("N0", ci)).Append('\n');
        sb.Append("  Hosts:     ").Append(summary.UniqueHosts.ToString("N0", ci)).Append('\n').Append('\n');

        sb.Append("Top devices\n");
        if (topClients.Count == 0)
        {
            sb.Append("  (none)\n");
        }
        foreach (var c in topClients)
        {
            sb.Append("  ").Append(c.ClientIp).Append("  ")
                .Append(c.Requests.ToString("N0", ci)).Append(" req, ")
                .Append((c.Bytes / (1024.0 * 1024)).ToString("N1", ci)).Append(" MB, ")
                .Append(c.DeniedRequests.ToString("N0", ci)).Append(" denied\n");
        }
        sb.Append('\n');

        sb.Append("Category breakdown\n");
        var categories = topHosts
            .GroupBy(h => _categorizer.Categorize(h.Host))
            .Select(g => new { Category = g.Key, Requests = g.Sum(x => x.Requests) })
            .OrderByDescending(x => x.Requests);
        var anyCategory = false;
        foreach (var entry in categories)
        {
            if (entry.Category == DomainCategory.Unknown)
            {
                continue;
            }

            anyCategory = true;
            sb.Append("  ").Append(entry.Category.ToString().PadRight(12))
                .Append(entry.Requests.ToString("N0", ci)).Append(" req\n");
        }
        if (!anyCategory)
        {
            sb.Append("  (no categorized traffic)\n");
        }
        sb.Append('\n');

        sb.Append("Open alerts: ").Append(alerts.Count.ToString("N0", ci)).Append('\n');
        foreach (var group in alerts.GroupBy(a => a.Type).OrderByDescending(g => g.Count()))
        {
            sb.Append("  ").Append(group.Key.ToString().PadRight(24))
                .Append(group.Count().ToString("N0", ci)).Append('\n');
        }

        return new DigestReport(title, fromUtc, toUtc, DateTime.UtcNow, sb.ToString());
    }
}
