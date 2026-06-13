using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Infrastructure.Blocklists;

namespace PiProxyGuard.Api.Controllers;

/// <summary>
/// Manages the allowlist — domains that must never be blocked. The allowlist
/// always overrides the blocklist, so adding or removing an entry rewrites the
/// Squid ACL immediately.
/// </summary>
[ApiController]
[Route("api/allowlist")]
public class AllowlistController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly BlocklistUpdater _updater;

    public AllowlistController(IUnitOfWork unitOfWork, BlocklistUpdater updater)
    {
        _unitOfWork = unitOfWork;
        _updater = updater;
    }

    /// <summary>Lists allowlisted domains, optionally filtered by substring.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AllowedDomainDto>>> GetAllowedDomains(
        [FromQuery] string? search,
        [FromQuery] int count = 100,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 1000);
        var domains = await _unitOfWork.AllowedDomains.SearchAsync(search, count, cancellationToken);
        return domains
            .Select(d => new AllowedDomainDto(d.Id, d.Domain, d.Reason, d.CreatedAtUtc))
            .ToList();
    }

    /// <summary>Adds an allowlist entry and rewrites the Squid ACL.</summary>
    [HttpPost]
    public async Task<ActionResult<AllowedDomainDto>> AddAllowedDomain(
        [FromBody] AddAllowedDomainRequest request, CancellationToken cancellationToken)
    {
        var domain = DomainUtils.NormalizeDomain(request.Domain);
        if (domain is null)
        {
            return BadRequest(new { error = $"'{request.Domain}' is not a valid domain." });
        }

        var existing = await _unitOfWork.AllowedDomains.FindByDomainAsync(domain, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new { error = $"Domain '{domain}' is already allowlisted (id {existing.Id})." });
        }

        var entity = new AllowedDomain
        {
            Domain = domain,
            Reason = request.Reason,
            CreatedAtUtc = DateTime.UtcNow
        };

        _unitOfWork.AllowedDomains.Add(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _updater.UpdateAsync(cancellationToken);

        var dto = new AllowedDomainDto(entity.Id, entity.Domain, entity.Reason, entity.CreatedAtUtc);
        return CreatedAtAction(nameof(GetAllowedDomains), new { search = entity.Domain }, dto);
    }

    /// <summary>Removes an allowlist entry (the domain can be blocked again).</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> RemoveAllowedDomain(long id, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.AllowedDomains.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        _unitOfWork.AllowedDomains.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _updater.UpdateAsync(cancellationToken);
        return NoContent();
    }
}
