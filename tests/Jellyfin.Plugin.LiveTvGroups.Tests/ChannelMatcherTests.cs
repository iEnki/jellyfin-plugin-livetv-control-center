using System;
using System.Linq;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class ChannelMatcherTests
{
    private static ChannelRef Ref(Guid id, string name, string? number = null) => new() { ItemId = id, Name = name, Number = number };

    [Fact]
    public void KeepsOrderAndMatchesById()
    {
        var a = new AvailableChannel(Guid.NewGuid(), "Das Erste", "1");
        var b = new AvailableChannel(Guid.NewGuid(), "ZDF", "2");

        var (channels, changed) = ChannelMatcher.Match([Ref(b.Id, "ZDF"), Ref(a.Id, "Das Erste")], [a, b]);

        Assert.Equal([b.Id, a.Id], channels.Select(c => c.Id));
        Assert.False(changed);
    }

    [Fact]
    public void RematchesByNameAndNumberAfterRescan()
    {
        var hdOne = new AvailableChannel(Guid.NewGuid(), "Sport HD", "10");
        var hdTwo = new AvailableChannel(Guid.NewGuid(), "sport hd ", "11");

        var (channels, changed) = ChannelMatcher.Match([Ref(Guid.NewGuid(), "Sport HD", "11")], [hdOne, hdTwo]);

        Assert.Equal(hdTwo.Id, Assert.Single(channels).Id);
        Assert.True(changed);
    }

    [Fact]
    public void AmbiguousNameWithoutNumberMatchIsSkipped()
    {
        var one = new AvailableChannel(Guid.NewGuid(), "News", "1");
        var two = new AvailableChannel(Guid.NewGuid(), "News", "2");

        var (channels, changed) = ChannelMatcher.Match([Ref(Guid.NewGuid(), "News", "9")], [one, two]);

        Assert.Empty(channels);
        Assert.False(changed);
    }

    [Fact]
    public void SkipsInaccessibleAndDuplicateChannels()
    {
        var a = new AvailableChannel(Guid.NewGuid(), "Arte", "5");

        var (channels, _) = ChannelMatcher.Match([Ref(a.Id, "Arte"), Ref(a.Id, "Arte"), Ref(Guid.NewGuid(), "Gesperrt")], [a]);

        Assert.Single(channels);
    }
}
