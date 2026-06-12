using PiProxyGuard.Domain.Enums;

namespace PiProxyGuard.Domain.Entities;

/// <summary>
/// An alert raised by the suspicious-activity detector for a client device.
/// </summary>
public class SuspiciousActivityAlert
{
    public long Id { get; set; }

    public string ClientIp { get; set; } = string.Empty;

    public AlertType Type { get; set; }

    /// <summary>Human-readable explanation, e.g. "1240 requests in 5 minutes (threshold 600)".</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Start of the analysis window the alert refers to.</summary>
    public DateTime WindowStartUtc { get; set; }

    /// <summary>End of the analysis window the alert refers to.</summary>
    public DateTime WindowEndUtc { get; set; }

    public DateTime DetectedAtUtc { get; set; }

    public bool IsAcknowledged { get; set; }
}
