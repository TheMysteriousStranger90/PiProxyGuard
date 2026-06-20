using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Infrastructure.Tunneling;

namespace PiProxyGuard.Api.Controllers;

/// <summary>
/// Manages the upstream tunnel — domains routed through the configured parent
/// proxy (cache_peer) instead of going out directly. Adding or removing an entry
/// rewrites the Squid tunnel ACL immediately so the change takes effect at once.
/// </summary>
[ApiController]
[Route("api/tunnel")]
public class TunnelController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TunnelAclUpdater _updater;

    public TunnelController(IUnitOfWork unitOfWork, TunnelAclUpdater updater)
    {
        _unitOfWork = unitOfWork;
        _updater = updater;
    }

    /// <summary>Lists tunneled domains, optionally filtered by substring.</summary>
    [HttpGet]
    public async Task<ActionResult<List<TunneledDomainDto>>> GetTunneledDomains(
        [FromQuery] string? search,
        [FromQuery] int count = 100,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 1000);
        var domains = await _unitOfWork.TunneledDomains.SearchAsync(search, count, cancellationToken);
        return domains
            .Select(d => new TunneledDomainDto(d.Id, d.Domain, d.Reason, d.CreatedAtUtc))
            .ToList();
    }

    /// <summary>Adds a tunnel entry and rewrites the Squid tunnel ACL.</summary>
    [HttpPost]
    public async Task<ActionResult<TunneledDomainDto>> AddTunneledDomain(
        [FromBody] AddTunneledDomainRequest request, CancellationToken cancellationToken)
    {
        var domain = DomainUtils.NormalizeDomain(request.Domain);
        if (domain is null)
        {
            return BadRequest(new { error = $"'{request.Domain}' is not a valid domain." });
        }

        var existing = await _unitOfWork.TunneledDomains.FindByDomainAsync(domain, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new { error = $"Domain '{domain}' is already tunneled (id {existing.Id})." });
        }

        var entity = new TunneledDomain
        {
            Domain = domain,
            Reason = request.Reason,
            CreatedAtUtc = DateTime.UtcNow
        };

        _unitOfWork.TunneledDomains.Add(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _updater.UpdateAsync(cancellationToken);

        var dto = new TunneledDomainDto(entity.Id, entity.Domain, entity.Reason, entity.CreatedAtUtc);
        return CreatedAtAction(nameof(GetTunneledDomains), new { search = entity.Domain }, dto);
    }

    /// <summary>Removes a tunnel entry (the domain goes out directly again).</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> RemoveTunneledDomain(long id, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.TunneledDomains.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        _unitOfWork.TunneledDomains.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _updater.UpdateAsync(cancellationToken);
        return NoContent();
    }
}
