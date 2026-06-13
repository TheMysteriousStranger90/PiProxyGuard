using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Infrastructure.Reports;

namespace PiProxyGuard.Api.Controllers;

/// <summary>
/// On-demand digest reports: a human-readable traffic-and-security summary for
/// a period (defaults to the last 24 hours). Use period=week for the last 7 days.
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly DigestReportBuilder _builder;

    public ReportsController(DigestReportBuilder builder) => _builder = builder;

    [HttpGet("digest")]
    public async Task<ActionResult<DigestReportDto>> GetDigest(
        [FromQuery] string period = "day",
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var to = toUtc ?? DateTime.UtcNow;
        var (from, title) = period.ToLowerInvariant() switch
        {
            "week" => (to.AddDays(-7), "PiProxyGuard weekly digest"),
            "month" => (to.AddDays(-30), "PiProxyGuard monthly digest"),
            _ => (to.AddHours(-24), "PiProxyGuard daily digest")
        };

        if (fromUtc is { } explicitFrom && explicitFrom < to)
        {
            from = explicitFrom;
        }

        var report = await _builder.BuildAsync(from, to, title, cancellationToken);
        return new DigestReportDto(report.Title, report.FromUtc, report.ToUtc, report.GeneratedAtUtc, report.PlainText);
    }
}
