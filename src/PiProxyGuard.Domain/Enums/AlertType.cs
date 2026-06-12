namespace PiProxyGuard.Domain.Enums;

public enum AlertType
{
    /// <summary>Too many requests from one client within the analysis window.</summary>
    HighRequestRate = 0,

    /// <summary>Too much traffic (bytes) consumed by one client within the window.</summary>
    HighTrafficVolume = 1,

    /// <summary>Repeated denied (TCP_DENIED / 403) requests from one client.</summary>
    RepeatedDeniedRequests = 2,

    /// <summary>Client contacted a domain present in the blocklist.</summary>
    BlockedDomainContact = 3
}
