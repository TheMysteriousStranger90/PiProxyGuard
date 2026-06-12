using PiProxyGuard.Infrastructure.Parsing;
using Xunit;

namespace PiProxyGuard.Tests;

public class LogFileTailerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tailer-{Guid.NewGuid():N}.log");

    [Fact]
    public void Reads_only_new_lines_between_polls()
    {
        File.WriteAllText(_path, "line-1\nline-2\n");

        var first = LogFileTailer.ReadNewLines(_path, 0, null);
        Assert.Equal(["line-1", "line-2"], first.Lines);

        File.AppendAllText(_path, "line-3\n");
        var second = LogFileTailer.ReadNewLines(_path, first.NewOffset, first.FirstLineFingerprint);

        Assert.Equal(["line-3"], second.Lines);
    }

    [Fact]
    public void Skips_partial_last_line_without_newline()
    {
        File.WriteAllText(_path, "complete\npartial-without-newline");

        var result = LogFileTailer.ReadNewLines(_path, 0, null);

        Assert.Equal(["complete"], result.Lines);
    }

    [Fact]
    public void Restarts_from_beginning_after_rotation()
    {
        File.WriteAllText(_path, "old-first-line\nold-second\n");
        var before = LogFileTailer.ReadNewLines(_path, 0, null);

        // Simulate logrotate: file replaced with new content.
        File.WriteAllText(_path, "new-first-line\n");
        var after = LogFileTailer.ReadNewLines(_path, before.NewOffset, before.FirstLineFingerprint);

        Assert.Equal(["new-first-line"], after.Lines);
    }

    [Fact]
    public void Returns_empty_for_missing_file()
    {
        var result = LogFileTailer.ReadNewLines("/nonexistent/access.log", 100, "x");

        Assert.Empty(result.Lines);
        Assert.Equal(0, result.NewOffset);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
