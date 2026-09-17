using System;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class GuideWindowTests
{
    private static readonly DateTime Now = new(2026, 9, 16, 18, 47, 12, DateTimeKind.Utc);

    [Fact]
    public void DefaultsToNowRoundedDownToHalfHour()
    {
        var (start, end) = GuideWindow.Normalize(null, null, Now);

        Assert.Equal(new DateTime(2026, 9, 16, 18, 30, 0, DateTimeKind.Utc), start);
        Assert.Equal(start + GuideWindow.DefaultLength, end);
    }

    [Fact]
    public void LimitsWindowTo24Hours()
    {
        var (start, end) = GuideWindow.Normalize(Now, Now.AddDays(3), Now);

        Assert.Equal(TimeSpan.FromHours(24), end - start);
    }

    [Fact]
    public void ReplacesInvertedWindow()
    {
        var (start, end) = GuideWindow.Normalize(Now, Now.AddHours(-2), Now);

        Assert.Equal(Now, start);
        Assert.Equal(Now + GuideWindow.DefaultLength, end);
    }
}
