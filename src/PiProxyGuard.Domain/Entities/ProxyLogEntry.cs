namespace PiProxyGuard.Domain.Entities;

/// <summary>
/// A single parsed line from the proxy access log (Squid native format).
/// </summary>
public class ProxyLogEntry
{
    public long Id { get; set; }

    /// <summary>Request timestamp (UTC).</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>Request duration reported by the proxy, in milliseconds.</summary>
    public int ElapsedMs { get; set; }

    /// <summary>IP address of the client device on the home network.</summary>
    public string ClientIp { get; set; } = string.Empty;

    /// <summary>Squid result code, e.g. TCP_MISS, TCP_HIT, TCP_DENIED.</summary>
    public string ResultCode { get; set; } = string.Empty;

    /// <summary>HTTP status code returned to the client.</summary>
    public int StatusCode { get; set; }

    /// <summary>Bytes sent to the client.</summary>
    public long Bytes { get; set; }

    /// <summary>HTTP method (GET, POST, CONNECT, ...).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Full request URL as logged.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Host extracted from the URL — what we aggregate statistics by.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>MIME type of the response, if logged.</summary>
    public string? ContentType { get; set; }

    /// <summary>True when the proxy denied the request (TCP_DENIED / 403).</summary>
    public bool WasDenied { get; set; }
}
