using System;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// Time window of the web program guide.
/// </summary>
public static class GuideWindow
{
    /// <summary>
    /// Default window length.
    /// </summary>
    public static readonly TimeSpan DefaultLength = TimeSpan.FromHours(6);

    /// <summary>
    /// Maximum window length.
    /// </summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(24);

    /// <summary>
    /// Normalizes a requested window: defaults to now rounded down to 30 minutes, at most 24 hours, end after start.
    /// </summary>
    /// <param name="start">Requested start (UTC).</param>
    /// <param name="end">Requested end (UTC).</param>
    /// <param name="now">Current time (UTC).</param>
    /// <returns>The normalized window in UTC.</returns>
    public static (DateTime Start, DateTime End) Normalize(DateTime? start, DateTime? end, DateTime now)
    {
        var from = (start ?? RoundDown(now)).ToUniversalTime();
        var to = (end ?? from + DefaultLength).ToUniversalTime();

        if (to <= from)
        {
            to = from + DefaultLength;
        }

        if (to - from > MaxLength)
        {
            to = from + MaxLength;
        }

        return (from, to);
    }

    private static DateTime RoundDown(DateTime value)
    {
        var utc = value.ToUniversalTime();
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute - (utc.Minute % 30), 0, DateTimeKind.Utc);
    }
}
