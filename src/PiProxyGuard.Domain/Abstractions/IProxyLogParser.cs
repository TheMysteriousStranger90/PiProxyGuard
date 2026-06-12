using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Domain.Abstractions;

/// <summary>
/// Parses one raw access-log line into a <see cref="ProxyLogEntry"/>.
/// Implementations exist per proxy log format (Squid native, 3proxy, ...).
/// </summary>
public interface IProxyLogParser
{
    /// <summary>
    /// Tries to parse a single log line. Returns false for empty,
    /// malformed or comment lines.
    /// </summary>
    bool TryParse(string line, out ProxyLogEntry? entry);
}
