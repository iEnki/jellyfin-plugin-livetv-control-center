using System;
using System.Linq;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

/// <summary>
/// Jellyfin's ChannelManager constructs every IChannel, and most live TV services depend on the
/// ChannelManager. Injecting them into the channel causes a circular dependency and the server does not start.
/// </summary>
public class StartupDependencyTests
{
    private static readonly Type[] AllowedDependencies = [typeof(GroupService), typeof(GroupStore), typeof(IServiceProvider)];

    [Theory]
    [InlineData(typeof(GroupsChannel))]
    [InlineData(typeof(GroupsMediaSourceProvider))]
    [InlineData(typeof(LiveTvStreamBridge))]
    [InlineData(typeof(AppGuideService))]
    [InlineData(typeof(GroupService))]
    [InlineData(typeof(NativeGuideService))]
    [InlineData(typeof(PlaylistSyncService))]
    public void OnlyResolvesJellyfinServicesLazily(Type type)
    {
        var parameters = Assert.Single(type.GetConstructors()).GetParameters();

        Assert.All(parameters, p => Assert.True(
            AllowedDependencies.Contains(p.ParameterType)
                || (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>)),
            $"{type.Name} must not inject {p.ParameterType.Name}"));
    }
}

public class ExternalIdTests
{
    [Fact]
    public void ChannelItemIdsAreUniquePerGroup()
    {
        var channel = Guid.NewGuid();
        var first = GroupsChannel.GetItemExternalId(Guid.NewGuid(), channel);
        var second = GroupsChannel.GetItemExternalId(Guid.NewGuid(), channel);

        Assert.NotEqual(first, second);
        Assert.Equal(channel.ToString("N"), first[^32..]);
    }
}
