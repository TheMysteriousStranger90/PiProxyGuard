using Microsoft.AspNetCore.Mvc;
using PiProxyGuard.Api.Contracts;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;

namespace PiProxyGuard.Api.Controllers;

/// <summary>
/// Looks a domain up against the configured threat-intelligence provider
/// (free URLhaus by default). Returns a clean verdict when the provider is
/// disabled, so callers always get a well-formed answer.
/// </summary>
[ApiController]
[Route("api/threat-intel")]
public class ThreatIntelController : ControllerBase
{
    private readonly IThreatIntelClient _client;

    public ThreatIntelController(IThreatIntelClient client) => _client = client;

    /// <summary>Checks whether a domain is known-malicious.</summary>
    [HttpGet("check")]
    public async Task<ActionResult<ThreatVerdictDto>> Check(
        [FromQuery] string domain, CancellationToken cancellationToken)
    {
        var normalized = DomainUtils.NormalizeDomain(domain);
        if (normalized is null)
        {
            return BadRequest(new { error = $"'{domain}' is not a valid domain." });
        }

        var verdict = await _client.CheckDomainAsync(normalized, cancellationToken);
        return new ThreatVerdictDto(verdict.Domain, verdict.IsMalicious, verdict.Source, verdict.Details);
    }
}
