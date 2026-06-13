using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;
using PiProxyGuard.Domain.Entities;
using PiProxyGuard.Domain.Enums;
using PiProxyGuard.Infrastructure.Blocklists;

namespace PiProxyGuard.Api.Controllers;

/// <summary>
/// Exports and restores the manual configuration (blocklist + allowlist) as a
/// portable JSON document, so a Pi can be re-imaged or migrated without losing
/// hand-curated rules. Feed-sourced entries are excluded from the export — they
/// re-download themselves — keeping the backup small and meaningful.
/// </summary>
[ApiController]
[Route("api/backup")]
public class BackupController : ControllerBase
{
    private const string BackupVersion = "1.2.0";

    private readonly IUnitOfWork _unitOfWork;
    private readonly BlocklistUpdater _updater;

    public BackupController(IUnitOfWork unitOfWork, BlocklistUpdater updater)
    {
        _unitOfWork = unitOfWork;
        _updater = updater;
    }

    /// <summary>Exports manual/auto blocklist entries and the full allowlist.</summary>
    [HttpGet("export")]
    public async Task<ActionResult<BackupDto>> Export(CancellationToken cancellationToken)
    {
        // Manual + auto entries only (feeds regenerate themselves).
        var manual = await _unitOfWork.BlockedDomains.SearchAsync(BlockSource.Manual, null, 100000, cancellationToken);
        var auto = await _unitOfWork.BlockedDomains.SearchAsync(BlockSource.Auto, null, 100000, cancellationToken);
        var allowed = await _unitOfWork.AllowedDomains.SearchAsync(null, 100000, cancellationToken);

        var blocked = manual.Concat(auto)
            .Select(d => new BackupBlockedDomainDto(d.Domain, d.Source.ToString(), d.Reason, d.IsActive, d.ExpiresAtUtc))
            .ToList();
        var allow = allowed
            .Select(a => new BackupAllowedDomainDto(a.Domain, a.Reason))
            .ToList();

        return new BackupDto(BackupVersion, DateTime.UtcNow, blocked, allow);
    }

    /// <summary>Imports a backup, adding entries that do not already exist, then rewrites the ACL.</summary>
    [HttpPost("import")]
    public async Task<ActionResult<RestoreResultDto>> Import(
        [FromBody] BackupDto backup, CancellationToken cancellationToken)
    {
        if (backup is null)
        {
            return BadRequest(new { error = "Empty backup payload." });
        }

        var now = DateTime.UtcNow;
        var blockedImported = 0;
        var allowedImported = 0;
        var skipped = 0;

        foreach (var item in backup.BlockedDomains ?? [])
        {
            var domain = DomainUtils.NormalizeDomain(item.Domain);
            if (domain is null || await _unitOfWork.BlockedDomains.FindByDomainAsync(domain, cancellationToken) is not null)
            {
                skipped++;
                continue;
            }

            var source = Enum.TryParse<BlockSource>(item.Source, true, out var parsed) ? parsed : BlockSource.Manual;
            _unitOfWork.BlockedDomains.Add(new BlockedDomain
            {
                Domain = domain,
                Source = source,
                Reason = item.Reason,
                IsActive = item.IsActive,
                ExpiresAtUtc = item.ExpiresAtUtc,
                CreatedAtUtc = now
            });
            blockedImported++;
        }

        foreach (var item in backup.AllowedDomains ?? [])
        {
            var domain = DomainUtils.NormalizeDomain(item.Domain);
            if (domain is null || await _unitOfWork.AllowedDomains.FindByDomainAsync(domain, cancellationToken) is not null)
            {
                skipped++;
                continue;
            }

            _unitOfWork.AllowedDomains.Add(new AllowedDomain
            {
                Domain = domain,
                Reason = item.Reason,
                CreatedAtUtc = now
            });
            allowedImported++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _updater.UpdateAsync(cancellationToken);

        return new RestoreResultDto(blockedImported, allowedImported, skipped);
    }
}
