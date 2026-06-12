using System.Text;

namespace PiProxyGuard.Infrastructure.Parsing;

public record TailResult(IReadOnlyList<string> Lines, long NewOffset, string? FirstLineFingerprint);

/// <summary>
/// Reads new lines from a log file starting at a stored byte offset.
/// Detects log rotation (file truncated or replaced) by comparing the
/// first line of the file with the stored fingerprint and restarts
/// from the beginning when rotation happened.
/// </summary>
public static class LogFileTailer
{
    public static TailResult ReadNewLines(string filePath, long offset, string? firstLineFingerprint)
    {
        if (!File.Exists(filePath))
        {
            return new TailResult([], 0, null);
        }

        using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var currentFingerprint = ReadFirstLine(stream);

        var rotated = stream.Length < offset ||
                      (firstLineFingerprint is not null && currentFingerprint != firstLineFingerprint);
        if (rotated)
        {
            offset = 0;
        }

        stream.Seek(offset, SeekOrigin.Begin);

        var lines = new List<string>();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

        long consumed = offset;
        while (reader.ReadLine() is { } line)
        {
            // Only count fully terminated lines; a partially written last line
            // (no trailing newline yet) is re-read on the next poll.
            var lineByteCount = Encoding.UTF8.GetByteCount(line);
            if (consumed + lineByteCount >= stream.Length)
            {
                break;
            }

            lines.Add(line);
            consumed += lineByteCount + 1; // +1 for '\n'
        }

        return new TailResult(lines, consumed, currentFingerprint);
    }

    private static string? ReadFirstLine(FileStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        var firstLine = reader.ReadLine();
        stream.Seek(0, SeekOrigin.Begin);
        return firstLine;
    }
}
