using Microsoft.Extensions.Logging;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Infrastructure.Squid;

namespace PiProxyGuard.Infrastructure.Tunneling;

/// <summary>
/// Regenerates the Squid upstream-tunnel ACL (tunnel_domains.acl) from the
/// current set of tunneled domains in the database and reconfigures Squid when
/// it changed. Shared by the API/dashboard (which edit the list) and the Worker
/// (which seeds the file on startup), mirroring how <c>BlocklistUpdater</c>
/// keeps blocked_domains.acl in sync.
/// </summary>
public class TunnelAclUpdater
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TunnelAclWriter _aclWriter;
    private readonly ILogger<TunnelAclUpdater> _logger;

    public TunnelAclUpdater(
        IUnitOfWork unitOfWork,
        TunnelAclWriter aclWriter,
        ILogger<TunnelAclUpdater> logger)
    {
        _unitOfWork = unitOfWork;
        _aclWriter = aclWriter;
        _logger = logger;
    }

    /// <summary>Rewrites the ACL from the database; returns true when it changed.</summary>
    public async Task<bool> UpdateAsync(CancellationToken cancellationToken)
    {
        var domains = await _unitOfWork.TunneledDomains.GetAllDomainNamesAsync(cancellationToken);
        var rewritten = await _aclWriter.WriteIfChangedAsync(domains, cancellationToken);

        _logger.LogInformation(
            "Upstream-tunnel update finished: {Count} domains, ACL rewritten: {Rewritten}",
            domains.Count, rewritten);

        return rewritten;
    }
}
