using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Infrastructure.Persistence;

namespace PiProxyGuard.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public AlertsController(AppDbContext dbContext) => _dbContext = dbContext;

    /// <summary>Lists alerts, newest first. Use onlyUnacknowledged=true to see open ones.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AlertDto>>> GetAlerts(
        [FromQuery] bool onlyUnacknowledged = false,
        [FromQuery] int count = 50,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 500);

        var query = _dbContext.Alerts.AsQueryable();
        if (onlyUnacknowledged)
        {
            query = query.Where(a => !a.IsAcknowledged);
        }

        return await query
            .OrderByDescending(a => a.DetectedAtUtc)
            .Take(count)
            .Select(a => new AlertDto(
                a.Id, a.ClientIp, a.Type.ToString(), a.Description,
                a.WindowStartUtc, a.WindowEndUtc, a.DetectedAtUtc, a.IsAcknowledged))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Marks an alert as acknowledged so it stops counting as open.</summary>
    [HttpPost("{id:long}/acknowledge")]
    public async Task<IActionResult> Acknowledge(long id, CancellationToken cancellationToken)
    {
        var alert = await _dbContext.Alerts.FindAsync([id], cancellationToken);
        if (alert is null)
        {
            return NotFound();
        }

        alert.IsAcknowledged = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
