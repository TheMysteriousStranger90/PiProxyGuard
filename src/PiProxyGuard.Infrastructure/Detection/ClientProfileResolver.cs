using System.Net;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Infrastructure.Detection;

/// <summary>Effective per-client detection thresholds after profile overrides.</summary>
public sealed record EffectiveThresholds(
    string? ProfileName,
    int MaxRequestsPerWindow,
    long MaxBytesPerWindow,
    int MaxDeniedPerWindow);

/// <summary>
/// Resolves the thresholds that apply to a given client IP. A device can match
/// a profile by exact IP or by CIDR range; the profile then either overrides a
/// threshold outright or scales the global default by its strictness factor
/// (less than 1 = stricter). Profiles are evaluated in declaration order and
/// the first match wins, so put specific entries before broad ranges.
/// </summary>
public class ClientProfileResolver
{
    private readonly DetectionOptions _options;
    private readonly List<(ClientProfileOptions Profile, IPAddress? Ip, IPNetwork? Network)> _compiled;

    public ClientProfileResolver(DetectionOptions options)
    {
        _options = options;
        _compiled = new List<(ClientProfileOptions, IPAddress?, IPNetwork?)>();

        foreach (var profile in options.ClientProfiles)
        {
            var match = profile.Match?.Trim();
            if (string.IsNullOrEmpty(match))
            {
                continue;
            }

            if (match.Contains('/', StringComparison.Ordinal) && IPNetwork.TryParse(match, out var network))
            {
                _compiled.Add((profile, null, network));
            }
            else if (IPAddress.TryParse(match, out var ip))
            {
                _compiled.Add((profile, ip, null));
            }
        }
    }

    public EffectiveThresholds Resolve(string clientIp)
    {
        var profile = FindProfile(clientIp);
        if (profile is null)
        {
            return new EffectiveThresholds(
                null,
                _options.MaxRequestsPerWindow,
                _options.MaxBytesPerWindow,
                _options.MaxDeniedPerWindow);
        }

        var strictness = profile.Strictness <= 0 ? 1.0 : profile.Strictness;

        var requests = profile.MaxRequestsPerWindow
            ?? Math.Max(1, (int)Math.Round(_options.MaxRequestsPerWindow * strictness));
        var bytes = profile.MaxBytesPerWindow
            ?? Math.Max(1L, (long)Math.Round(_options.MaxBytesPerWindow * strictness));
        var denied = profile.MaxDeniedPerWindow
            ?? Math.Max(1, (int)Math.Round(_options.MaxDeniedPerWindow * strictness));

        return new EffectiveThresholds(profile.Name ?? profile.Match, requests, bytes, denied);
    }

    private ClientProfileOptions? FindProfile(string clientIp)
    {
        if (!IPAddress.TryParse(clientIp, out var address))
        {
            return null;
        }

        foreach (var (profile, ip, network) in _compiled)
        {
            if (ip is not null && ip.Equals(address))
            {
                return profile;
            }

            if (network is { } net && net.Contains(address))
            {
                return profile;
            }
        }

        return null;
    }
}
