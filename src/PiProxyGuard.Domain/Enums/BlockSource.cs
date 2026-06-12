namespace PiProxyGuard.Domain.Enums;

public enum BlockSource
{
    /// <summary>Added by hand through the Web API.</summary>
    Manual = 0,

    /// <summary>Downloaded from a remote blocklist feed.</summary>
    Feed = 1,

    /// <summary>Added automatically by the suspicious-activity detector.</summary>
    Auto = 2
}
