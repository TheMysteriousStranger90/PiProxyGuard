namespace PiProxyGuard.Domain.Categorization;

/// <summary>
/// Coarse traffic categories assigned to a destination host by the
/// rule-based <see cref="Abstractions.IDomainCategorizer"/>. Lets the stats
/// layer answer "how much of my traffic is ads/trackers vs streaming".
/// </summary>
public enum DomainCategory
{
    /// <summary>Could not be classified.</summary>
    Unknown = 0,

    /// <summary>Advertising networks and ad exchanges.</summary>
    Advertising = 1,

    /// <summary>Analytics / behavioural trackers / telemetry.</summary>
    Tracking = 2,

    /// <summary>Social networks and their widgets.</summary>
    Social = 3,

    /// <summary>Video/audio streaming services.</summary>
    Streaming = 4,

    /// <summary>Content delivery networks and static asset hosts.</summary>
    Cdn = 5,

    /// <summary>Known malware / phishing / command-and-control.</summary>
    Malware = 6
}
