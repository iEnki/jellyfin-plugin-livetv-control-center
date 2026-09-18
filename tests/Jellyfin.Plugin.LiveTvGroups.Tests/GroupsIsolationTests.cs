using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class GroupsIsolationTests
{
    [Fact]
    public void RegistersOnlyExplicitNativeScopeFilterWithoutStartupMiddleware()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IStartupFilter));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(Jellyfin.Plugin.LiveTvGroups.Api.NativeGuideChannelFilter));
    }

    [Fact]
    public void OldUserDataPreservesGroupsAndCannotActivateNativeFiltering()
    {
        var group = Guid.NewGuid();
        var data = JsonSerializer.Deserialize<UserGroups>("{\"Revision\":12,\"ActiveGuideGroupId\":\""+group+"\",\"Groups\":[{\"Id\":\""+group+"\",\"Name\":\"Crime\"}]}")!;
        Assert.Equal(group, Assert.Single(data.Groups).Id);
        Assert.Equal(12, data.Revision);
        Assert.Empty(data.Preferences.HiddenGroupIds);
        Assert.Equal("guide", data.Preferences.DefaultView);
        Assert.Empty(data.NativeGuideScopes);
        Assert.DoesNotContain("ActiveGuideGroupId", JsonSerializer.Serialize(data), StringComparison.Ordinal);
    }

    [Fact]
    public void PreferencesPersistPerUserWithoutModifyingGroupChannels()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ltvg-"+Guid.NewGuid().ToString("N"));
        try
        {
            var alice=Guid.NewGuid(); var bob=Guid.NewGuid(); var group=Guid.NewGuid();var channel=Guid.NewGuid();
            var store=new GroupStore(directory);
            store.Update(alice, doc => { doc.Groups.Add(new ChannelGroup {Id=group,Name="Crime",Channels=[new ChannelRef{ItemId=channel,Name="RTL Crime"}]});doc.Preferences=new GroupPreferences{HiddenGroupIds=[group],Zoom=8,DefaultView="channels"};return true; });
            var reloaded=new GroupStore(directory);
            Assert.Equal(group,Assert.Single(reloaded.Get(alice).Preferences.HiddenGroupIds));
            Assert.Equal(channel,Assert.Single(Assert.Single(reloaded.Get(alice).Groups).Channels).ItemId);
            Assert.Empty(reloaded.Get(bob).Preferences.HiddenGroupIds);
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }

    [Fact]
    public void GuideKeepsOnlyValidOverlappingProgramsOfAllowedChannels()
    {
        var channel=Guid.NewGuid();var other=Guid.NewGuid();var start=new DateTime(2026,9,17,10,0,0,DateTimeKind.Utc);var end=start.AddHours(6);
        BaseItemDto Program(Guid? source,DateTime? from,DateTime? to) => new(){Id=Guid.NewGuid(),ChannelId=source,StartDate=from,EndDate=to};
        var valid=Program(channel,start.AddHours(-2),start.AddHours(1));
        var programs=new[]{valid,valid,Program(other,start,end),Program(null,start,end),Program(channel,null,end),Program(channel,end,start),Program(channel,end,end.AddHours(1)),Program(channel,start.AddHours(-1),start)};
        Assert.Same(valid,Assert.Single(GroupGuideService.ValidPrograms(programs, new[]{channel}.ToHashSet(),start,end)));
    }
}
