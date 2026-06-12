using System.Globalization;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Common;
using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Infrastructure.Parsing;

/// <summary>
/// Parses the Squid native access log format:
/// <code>
/// time.ms elapsed client result/status bytes method URL user hierarchy/peer type
/// 1718100000.123  245 192.168.1.10 TCP_MISS/200 51234 GET http://example.com/x - HIER_DIRECT/93.184.216.34 text/html
/// </code>
/// </summary>
public class SquidAccessLogParser : IProxyLogParser
{
    private const int MinFieldCount = 7;

    public bool TryParse(string line, out ProxyLogEntry? entry)
    {
        entry = null;

        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            return false;
        }

        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < MinFieldCount)
        {
            return false;
        }

        if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var unixSeconds) ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var elapsedMs) ||
            !long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
        {
            return false;
        }

        var resultParts = fields[3].Split('/');
        if (resultParts.Length != 2 || !int.TryParse(resultParts[1], out var statusCode))
        {
            return false;
        }

        var resultCode = resultParts[0];
        var url = fields[6];
        var host = DomainUtils.ExtractHost(url);

        entry = new ProxyLogEntry
        {
            TimestampUtc = DateTime.UnixEpoch.AddSeconds(unixSeconds),
            ElapsedMs = Math.Max(0, elapsedMs),
            ClientIp = fields[2],
            ResultCode = resultCode,
            StatusCode = statusCode,
            Bytes = Math.Max(0, bytes),
            Method = fields[5],
            Url = url.Length <= 2048 ? url : url[..2048],
            Host = host,
            ContentType = fields.Length >= 10 && fields[9] != "-" ? fields[9] : null,
            WasDenied = resultCode.Contains("DENIED", StringComparison.OrdinalIgnoreCase) || statusCode == 403
        };

        return true;
    }
}
