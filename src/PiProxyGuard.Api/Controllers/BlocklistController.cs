using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Common;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Blocklists;
using PiProxyGuard.Infrastructure.Persistence;

namespace PiProxyGuard.Api.Controllers;

[ApiController]
[Route("api/blocklist")]
public class BlocklistController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly BlocklistUpdater _updater;

    public BlocklistController(AppDbContext dbContext, BlocklistUpdater updater)
    {
        _dbContext = dbContext;
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
        var query = _dbContext.BlockedDomains.AsQueryable();

        if (!string.IsNullOrEmpty(source) && Enum.TryParse<BlockSource>(source, true, out var parsedSource))
        {
            query = query.Where(d => d.Source == parsedSource);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(d => d.Domain.Contains(search));
        }

        return await query
            .OrderBy(d => d.Domain)
            .Take(count)
            .Select(d => new BlockedDomainDto(
                d.Id, d.Domain, d.Source.ToString(), d.Reason, d.CreatedAtUtc, d.IsActive))
            .ToListAsync(cancellationToken);
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

        var existing = await _dbContext.BlockedDomains
            .FirstOrDefaultAsync(d => d.Domain == domain, cancellationToken);
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
            IsActive = true
        };

        _dbContext.BlockedDomains.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _updater.UpdateAsync(cancellationToken);

        var dto = new BlockedDomainDto(
            entity.Id, entity.Domain, entity.Source.ToString(), entity.Reason, entity.CreatedAtUtc, entity.IsActive);
        return CreatedAtAction(nameof(GetBlockedDomains), new { search = entity.Domain }, dto);
    }

    /// <summary>Removes a manually blocked domain (feed entries reappear on the next update).</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> RemoveBlockedDomain(long id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.BlockedDomains.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        _dbContext.BlockedDomains.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
