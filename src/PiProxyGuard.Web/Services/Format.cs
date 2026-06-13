using System.Globalization;

namespace PiProxyGuard.Web.Services;

/// <summary>Small display helpers shared by the dashboard components.</summary>
public static class Format
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Human-readable byte size, e.g. 1536 -&gt; "1.5 KB".</summary>
    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < ByteUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {ByteUnits[unit]}"
            : value.ToString("0.#", CultureInfo.InvariantCulture) + " " + ByteUnits[unit];
    }

    /// <summary>Thousands-separated integer, e.g. 12345 -&gt; "12,345".</summary>
    public static string Number(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>Compact "x minutes ago" style relative time from a UTC instant.</summary>
    public static string Ago(DateTime utc)
    {
        var delta = DateTime.UtcNow - utc;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 60)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} min ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours} h ago";
        }

        return $"{(int)delta.TotalDays} d ago";
    }

    /// <summary>Short UTC timestamp for tables.</summary>
    public static string Timestamp(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
}
