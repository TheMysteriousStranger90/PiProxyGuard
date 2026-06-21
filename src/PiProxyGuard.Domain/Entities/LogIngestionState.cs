namespace PiProxyGuard.Domain.Entities;

/// <summary>
/// Tracks how far into the access log file the ingestion worker has read,
/// so it can resume after a restart and detect log rotation.
/// </summary>
public class LogIngestionState
{
    public long Id { get; set; }

    public string FilePath { get; set; } = string.Empty;

    /// <summary>Byte offset of the next unread position in the file.</summary>
    public long Offset { get; set; }

    /// <summary>First line of the file when the offset was recorded — used to detect rotation.</summary>
    public string? FirstLineFingerprint { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
