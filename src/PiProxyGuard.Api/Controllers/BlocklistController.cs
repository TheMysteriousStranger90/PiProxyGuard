using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Blocklists;

namespace PiProxyGuard.Api.Controllers;

[ApiController]
[Route("api/blocklist")]
public class BlocklistController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly BlocklistUpdater _updater;

    public BlocklistController(IUnitOfWork unitOfWork, BlocklistUpdater updater)
    {
        _unitOfWork = unitOfWork;
        _updater = updater;
    }

    /// <summary>Lists blocked domains. Filter with source=Manual|Feed|Auto, search by substring.</summary>
    [HttpGet]
    public async Task<ActionResult<List<BlockedDomainDto>>> GetBlockedDomains(
        [FromQuery] string? source,
        [FromQuery] string? search,
        [FromQuery] int count = 100,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 1000);

        BlockSource? parsedSource = null;
        if (!string.IsNullOrEmpty(source) && Enum.TryParse<BlockSource>(source, true, out var value))
        {
            parsedSource = value;
        }

        var domains = await _unitOfWork.BlockedDomains.SearchAsync(parsedSource, search, count, cancellationToken);

        return domains
            .Select(d => new BlockedDomainDto(
                d.Id, d.Domain, d.Source.ToString(), d.Reason, d.CreatedAtUtc, d.IsActive, d.ExpiresAtUtc))
            .ToList();
    }

    /// <summary>Adds a manually blocked domain and rewrites the Squid ACL.</summary>
    [HttpPost]
    public async Task<ActionResult<BlockedDomainDto>> AddBlockedDomain(
        [FromBody] AddBlockedDomainRequest request, CancellationToken cancellationToken)
    {
        var domain = DomainUtils.NormalizeDomain(request.Domain);
        if (domain is null)
        {
            return BadRequest(new { error = $"'{request.Domain}' is not a valid domain." });
        }

        var existing = await _unitOfWork.BlockedDomains.FindByDomainAsync(domain, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new { error = $"Domain '{domain}' is already in the blocklist (id {existing.Id})." });
        }

        var entity = new BlockedDomain
        {
            Domain = domain,
            Source = BlockSource.Manual,
            Reason = request.Reason,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            ExpiresAtUtc = request.ExpiresInHours is > 0 ? DateTime.UtcNow.AddHours(request.ExpiresInHours.Value) : null
        };

        _unitOfWork.BlockedDomains.Add(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _updater.UpdateAsync(cancellationToken);

        var dto = new BlockedDomainDto(
            entity.Id, entity.Domain, entity.Source.ToString(), entity.Reason, entity.CreatedAtUtc, entity.IsActive, entity.ExpiresAtUtc);
        return CreatedAtAction(nameof(GetBlockedDomains), new { search = entity.Domain }, dto);
    }

    /// <summary>Removes a manually blocked domain (feed entries reappear on the next update).</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> RemoveBlockedDomain(long id, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BlockedDomains.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        _unitOfWork.BlockedDomains.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _updater.UpdateAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Forces an immediate re-download of all blocklist feeds.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<object>> Refresh(CancellationToken cancellationToken)
    {
        var result = await _updater.UpdateAsync(cancellationToken);
        return Ok(new
        {
            result.TotalActiveDomains,
            result.FeedDomains,
            result.AclRewritten
        });
    }
}
