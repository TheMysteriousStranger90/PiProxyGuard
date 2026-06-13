using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public AlertsController(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    /// <summary>Lists alerts, newest first. Use onlyUnacknowledged=true to see open ones.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AlertDto>>> GetAlerts(
        [FromQuery] bool onlyUnacknowledged = false,
        [FromQuery] int count = 50,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 500);

        var alerts = await _unitOfWork.Alerts.GetAlertsAsync(onlyUnacknowledged, count, cancellationToken);

        return alerts
            .Select(a => new AlertDto(
                a.Id, a.ClientIp, a.Type.ToString(), a.Description,
                a.WindowStartUtc, a.WindowEndUtc, a.DetectedAtUtc, a.IsAcknowledged))
            .ToList();
    }

    /// <summary>Marks an alert as acknowledged so it stops counting as open.</summary>
    [HttpPost("{id:long}/acknowledge")]
    public async Task<IActionResult> Acknowledge(long id, CancellationToken cancellationToken)
    {
        var alert = await _unitOfWork.Alerts.GetByIdAsync(id, cancellationToken);
        if (alert is null)
        {
            return NotFound();
        }

        alert.IsAcknowledged = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
