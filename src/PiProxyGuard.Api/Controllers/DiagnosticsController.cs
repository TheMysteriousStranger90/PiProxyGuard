using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Infrastructure.Diagnostics;

namespace PiProxyGuard.Api.Controllers;

/// <summary>
/// Self-diagnostics: database reachable, log ingestion fresh, ACL file present,
/// blocklist populated. Returns HTTP 200 when healthy and 503 when any check is
/// in the Error state, so it doubles as a container/orchestrator readiness probe.
/// </summary>
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly SystemDiagnostics _diagnostics;

    public DiagnosticsController(SystemDiagnostics diagnostics) => _diagnostics = diagnostics;

    [HttpGet]
    public async Task<ActionResult<DiagnosticsReportDto>> Get(CancellationToken cancellationToken)
    {
        var report = await _diagnostics.RunAsync(cancellationToken);
        var dto = new DiagnosticsReportDto(
            report.Status.ToString(),
            report.IsHealthy,
            report.GeneratedAtUtc,
            report.Checks.Select(c => new DiagnosticCheckDto(c.Name, c.Status.ToString(), c.Detail)).ToList());

        return report.IsHealthy
            ? Ok(dto)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, dto);
    }
}
