using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class NativeAppTests
{
    [Fact]
    public async Task BridgeUsesHostExportsOnceInsteadOfUnregisteredNativeDiProvider()
    {
        var exports = 0;
        var stream = InterfaceStub.Create<ILiveStream>((method, args) => null);
        var native = InterfaceStub.Create<IMediaSourceProvider>((method, args) => method.Name switch
        {
            "GetMediaSources" => Task.FromResult<IEnumerable<MediaSourceInfo>>([new MediaSourceInfo { Id = "native" }]),
            "OpenMediaSource" => Task.FromResult(stream),
            _ => throw new NotSupportedException(method.Name)
        });
        var host = InterfaceStub.Create<IServerApplicationHost>((method, args) =>
        {
            Assert.Equal("GetExports", method.Name); exports++; return new[] { native };
        });
        using var services = new ServiceCollection().AddSingleton(host).BuildServiceProvider();
        var bridge = new LiveTvStreamBridge(services);
        Assert.Equal("native", Assert.Single(await bridge.GetMediaSources(new LiveTvChannel(), CancellationToken.None)).Id);
        Assert.Same(stream, await bridge.OpenMediaSource("native_token", [], CancellationToken.None));
        Assert.Equal(1, exports);
    }

    [Fact]
    public async Task GroupPlaybackUsesNativeOpenAndCloseWithoutChangingNativeSource()
    {
        using var f = new Fixture();
        var sources = await f.Provider.GetMediaSources(f.Video(), CancellationToken.None);
        var source = Assert.Single(sources);
        Assert.True(source.RequiresOpening);
        Assert.True(source.RequiresClosing);
        Assert.True(source.IsInfiniteStream);
        Assert.Null(source.LiveStreamId);
        Assert.False(f.Bridge.Source.RequiresOpening);
        Assert.Null(f.Bridge.Source.OpenToken);
        var streams = new List<ILiveStream>();
        var opened = await f.Provider.OpenMediaSource(source.OpenToken, streams, CancellationToken.None);
        Assert.Same(f.Bridge.Stream, opened);
        Assert.Same(streams, f.Bridge.Streams);
        Assert.Equal("LiveTvChannel_" + f.Channel.Id.ToString("N") + "_native_source", f.Bridge.Token);
        await opened.Close();
        Assert.True(f.Bridge.Closed);
    }

    [Fact]
    public async Task MissingNativeSourceIdGetsStableClientIdWithoutChangingTunerSelection()
    {
        using var f = new Fixture(); f.Bridge.Source.Id = null; f.Bridge.Source.OpenToken = string.Empty;
        var first = Assert.Single(await f.Provider.GetMediaSources(f.Video(), CancellationToken.None));
        var second = Assert.Single(await f.Provider.GetMediaSources(f.Video(), CancellationToken.None));
        Assert.Equal(first.Id, second.Id); Assert.Equal(32, first.Id.Length);
        Assert.Null(f.Bridge.Source.Id);
        await f.Provider.OpenMediaSource(first.OpenToken, [], CancellationToken.None);
        Assert.Equal("LiveTvChannel_" + f.Channel.Id.ToString("N") + "_", f.Bridge.Token);
    }

    [Fact]
    public async Task OriginalLiveTvAndOtherPluginChannelsGetNoAdditionalSources()
    {
        using var f = new Fixture();
        Assert.Empty(await f.Provider.GetMediaSources(f.Channel, CancellationToken.None));
        var foreign = f.Video(); foreign.ChannelId = Guid.NewGuid();
        Assert.Empty(await f.Provider.GetMediaSources(foreign, CancellationToken.None));
        Assert.Equal(0, f.Bridge.Reads);
    }

    [Fact]
    public async Task PlaybackCannotUseOtherUsersGroupsOrForbiddenChannels()
    {
        using var f = new Fixture();
        var unknownGroup = f.Video(); unknownGroup.ExternalId = GroupsChannel.GetItemExternalId(Guid.NewGuid(), f.Channel.Id);
        var forbidden = f.Video(); forbidden.ExternalId = GroupsChannel.GetItemExternalId(f.Group, f.Forbidden.Id);
        Assert.Empty(await f.Provider.GetMediaSources(unknownGroup, CancellationToken.None));
        Assert.Empty(await f.Provider.GetMediaSources(forbidden, CancellationToken.None));
        f.Http.HttpContext!.User = new ClaimsPrincipal();
        Assert.Empty(await f.Provider.GetMediaSources(f.Video(), CancellationToken.None));
        Assert.Equal(0, f.Bridge.Reads);
    }

    [Fact]
    public async Task OpeningRechecksUserAndChangedPermissions()
    {
        using var f = new Fixture();
        var source = Assert.Single(await f.Provider.GetMediaSources(f.Video(), CancellationToken.None));
        f.Authenticate(Guid.NewGuid());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Provider.OpenMediaSource(source.OpenToken, [], CancellationToken.None));
        f.Authenticate(f.User.Id);
        f.Accessible = [];
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Provider.OpenMediaSource(source.OpenToken, [], CancellationToken.None));
        Assert.Null(f.Bridge.Token);
    }

    [Fact]
    public async Task OpeningRechecksDeletedGroupsAndRejectsMalformedTokens()
    {
        using var f = new Fixture();
        var source = Assert.Single(await f.Provider.GetMediaSources(f.Video(), CancellationToken.None));
        f.Store.Update(f.User.Id, doc => { doc.Groups.Clear(); return true; });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Provider.OpenMediaSource(source.OpenToken, [], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Provider.OpenMediaSource("not-base64", [], CancellationToken.None));
        Assert.Null(f.Bridge.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ltvch")]
    [InlineData("ltvch4_bad_bad")]
    [InlineData("unrelated_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("ltvepglive_bad_bad_bad")]
    public void MalformedMediaIdsNeverThrowOrMatch(string? id)
        => Assert.False(GroupItemId.TryParse(id, out _, out _));

    [Fact]
    public async Task NativeGroupGuideCanBeBrowsedAndItsLiveChildUsesSamePlaybackBridge()
    {
        using var f = new Fixture();
        var channelProvider = new GroupsChannel(f.Groups, f.Services, NullLogger<GroupsChannel>.Instance);
        var folder = await channelProvider.GetChannelItems(new InternalChannelItemQuery { UserId = f.User.Id, FolderId = GroupsChannel.GetFolderExternalId(f.Group) }, CancellationToken.None);
        Assert.Equal(2, folder.Items.Count);
        var guideRoot = folder.Items.Single(i => i.Type == ChannelItemType.Folder);
        Assert.Equal("Fernsehprogramm", guideRoot.Name);
        var guide = new AppGuideService(f.Groups, f.Services);
        var days = await guide.GetItems(f.User, guideRoot.Id, CancellationToken.None);
        Assert.Equal(7, days.Items.Count);
        var channels = await guide.GetItems(f.User, days.Items[0].Id, CancellationToken.None);
        var sender = Assert.Single(channels.Items);
        Assert.Contains(f.Channel.Name, sender.Name, StringComparison.Ordinal);
        var programs = await guide.GetItems(f.User, sender.Id, CancellationToken.None);
        var program = Assert.Single(programs.Items);
        Assert.Contains("Allowed program", program.Name, StringComparison.Ordinal);
        Assert.Contains("Description", program.Overview, StringComparison.Ordinal);
        var detail = await guide.GetItems(f.User, program.Id, CancellationToken.None);
        var live = Assert.Single(detail.Items);
        Assert.True(live.IsLiveStream);
        Assert.Contains("live ansehen", live.Name, StringComparison.Ordinal);
        Assert.Contains("keine Aufnahme", live.Overview, StringComparison.Ordinal);
        Assert.True(GroupItemId.TryParse(live.Id, out var groupId, out var channelId));
        Assert.Equal(f.Group, groupId); Assert.Equal(f.Channel.Id, channelId);
        var video = f.Video(); video.ExternalId = live.Id;
        var source = Assert.Single(await f.Provider.GetMediaSources(video, CancellationToken.None));
        Assert.Same(f.Bridge.Stream, await f.Provider.OpenMediaSource(source.OpenToken, [], CancellationToken.None));
    }

    [Fact]
    public async Task ChangedProgramMetadataCreatesFreshNativeDetailItems()
    {
        using var f = new Fixture(); var guide = new AppGuideService(f.Groups, f.Services);
        var days = await guide.GetItems(f.User, AppGuideService.GetRootId(f.Group), CancellationToken.None);
        var sender = Assert.Single((await guide.GetItems(f.User, days.Items[0].Id, CancellationToken.None)).Items);
        var before = Assert.Single((await guide.GetItems(f.User, sender.Id, CancellationToken.None)).Items);
        var beforeLive = Assert.Single((await guide.GetItems(f.User, before.Id, CancellationToken.None)).Items);
        f.ProgramName = "Changed program";
        var after = Assert.Single((await guide.GetItems(f.User, sender.Id, CancellationToken.None)).Items);
        var afterLive = Assert.Single((await guide.GetItems(f.User, after.Id, CancellationToken.None)).Items);
        Assert.NotEqual(before.Id, after.Id); Assert.NotEqual(beforeLive.Id, afterLive.Id);
        Assert.Contains(f.ProgramName, afterLive.Overview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeGuideCannotBrowseUnknownGroupsOrInvalidRoutes()
    {
        using var f = new Fixture();
        var guide = new AppGuideService(f.Groups, f.Services);
        Assert.Empty((await guide.GetItems(f.User, AppGuideService.GetRootId(Guid.NewGuid()), CancellationToken.None)).Items);
        Assert.False(AppGuideService.TryParse("ltvepgchannel_bad_20269999_bad", out _));
        Assert.False(AppGuideService.TryParse("ltvepgday_" + f.Group.ToString("N") + "_20260917_extra", out _));
    }

    [Theory]
    [InlineData(2026, 3, 29, 23)]
    [InlineData(2026, 10, 25, 25)]
    public void AppGuideUsesFullLocalCalendarDaysAcrossDst(int year, int month, int day, int hours)
    {
        var zone = AppGuideService.GetTimeZone("Europe/Vienna");
        var (start, end) = AppGuideService.DayWindow(new DateTime(year, month, day), zone);
        Assert.Equal(hours, (end - start).TotalHours);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public async Task SharedGroupsProvideEpgForNormalUserAndRevocationBlocksNativeGuideAndOpenToken()
    {
        using var f = new Fixture();
        Assert.False(f.User.HasPermission(PermissionKind.IsAdministrator));
        f.Store.UpdateAdministration(config => { config.Mode = "shared"; config.Groups = f.Store.Get(f.User.Id).Groups; return true; });
        var group = Assert.Single(f.Groups.GetGroups(f.User));
        var guide = await f.Services.GetRequiredService<GroupGuideService>().GetGuide(f.User, [group], null, null, CancellationToken.None);
        Assert.Equal(f.Channel.Id, Assert.Single(guide.Channels).Id);
        Assert.Equal("Allowed program", Assert.Single(guide.Programs).Name);
        var provider = new GroupsChannel(f.Groups, f.Services, NullLogger<GroupsChannel>.Instance);
        var before = provider.GetCacheKey(f.User.Id.ToString());
        Assert.Single((await provider.GetChannelItems(new() { UserId = f.User.Id }, CancellationToken.None)).Items);
        var source = Assert.Single(await f.Provider.GetMediaSources(f.Video(), CancellationToken.None));
        f.Store.UpdateAdministration(config => { config.Groups[0].DeniedUserIds = [f.User.Id]; return true; });
        Assert.NotEqual(before, provider.GetCacheKey(f.User.Id.ToString()));
        Assert.Empty((await provider.GetChannelItems(new() { UserId = f.User.Id }, CancellationToken.None)).Items);
        Assert.Empty((await provider.GetChannelItems(new() { UserId = f.User.Id, FolderId = GroupsChannel.GetFolderExternalId(f.Group) }, CancellationToken.None)).Items);
        Assert.Empty((await f.Services.GetRequiredService<AppGuideService>().GetItems(f.User, AppGuideService.GetRootId(f.Group), CancellationToken.None)).Items);
        Assert.Empty(await f.Provider.GetMediaSources(f.Video(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Provider.OpenMediaSource(source.OpenToken, [], CancellationToken.None));
        Assert.Empty(f.Groups.ResolveChannels(f.User, group, f.Groups.GetAccessibleChannels(f.User)));
        Assert.Null(f.Bridge.Token);
    }

    [Fact]
    public async Task NativeEnglishGuideUsesEnglishLabelsAndSeparatesLanguageCache()
    {
        using var f = new Fixture();
        var provider = new GroupsChannel(f.Services.GetRequiredService<GroupService>(), f.Services, NullLogger<GroupsChannel>.Instance);
        var germanCache = provider.GetCacheKey(f.User.Id.ToString());
        f.Http.HttpContext!.Request.Headers.AcceptLanguage = "en-US";
        Assert.NotEqual(germanCache, provider.GetCacheKey(f.User.Id.ToString()));
        var folder = await provider.GetChannelItems(new InternalChannelItemQuery { UserId=f.User.Id,FolderId=GroupsChannel.GetFolderExternalId(f.Group) }, CancellationToken.None);
        var root = folder.Items.Single(i=>i.Type==ChannelItemType.Folder);
        Assert.Equal("TV guide",root.Name);
        var guide=f.Services.GetRequiredService<AppGuideService>();
        var days=await guide.GetItems(f.User,root.Id,CancellationToken.None);
        Assert.StartsWith("Today",days.Items[0].Name,StringComparison.Ordinal);
        var channel=Assert.Single((await guide.GetItems(f.User,days.Items[0].Id,CancellationToken.None)).Items);
        var program=Assert.Single((await guide.GetItems(f.User,channel.Id,CancellationToken.None)).Items);
        var live=Assert.Single((await guide.GetItems(f.User,program.Id,CancellationToken.None)).Items);
        Assert.Contains("watch live",live.Name,StringComparison.Ordinal);
        Assert.Contains("not a recording",live.Overview,StringComparison.Ordinal);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ltvg-native-" + Guid.NewGuid().ToString("N"));
        public readonly User User = new("alice", "auth", "reset");
        public readonly Guid Group = Guid.NewGuid(), Root = Guid.NewGuid(), ProgramId = Guid.NewGuid();
        public readonly LiveTvChannel Channel = new() { Id = Guid.NewGuid(), Name = "Allowed", ExternalId = "m3u_allowed" };
        public readonly LiveTvChannel Forbidden = new() { Id = Guid.NewGuid(), Name = "Forbidden", ExternalId = "m3u_forbidden" };
        public List<BaseItem> Accessible;
        public string ProgramName = "Allowed program";
        public readonly HttpContextAccessor Http = new() { HttpContext = new DefaultHttpContext() };
        public readonly StubBridge Bridge = new();
        public readonly ServiceProvider Services;
        public readonly GroupStore Store;
        public readonly GroupService Groups;
        public readonly GroupsMediaSourceProvider Provider;
        public Fixture()
        {
            User.SetPermission(PermissionKind.EnableLiveTvAccess, true);
            Authenticate(User.Id); Http.HttpContext!.Request.Headers.AcceptLanguage = "de-DE";
            Accessible = [Channel];
            Store = new GroupStore(_directory);
            Store.Update(User.Id, doc => { doc.Groups.Add(new ChannelGroup { Id = Group, Name = "Crime", Channels = [GroupService.ToRef(Channel), GroupService.ToRef(Forbidden)] }); return true; });
            var services = new ServiceCollection();
            services.AddSingleton(Store).AddSingleton<GroupService>().AddSingleton<GroupGuideService>().AddSingleton<AppGuideService>();
            services.AddSingleton<IHttpContextAccessor>(Http).AddSingleton<LiveTvStreamBridge>(Bridge);
            services.AddSingleton(InterfaceStub.Create<IUserManager>((method, args) => method.Name == "GetUserById" && Equals(args![0], User.Id) ? User : null));
            services.AddSingleton(InterfaceStub.Create<ILibraryManager>((method, args) => method.Name == "GetNewItemId" ? Root : method.Name == "GetItemById" && Equals(args![0], Root) ? new Folder { Id = Root, Name = "Custom group display name" } : null));
            services.AddSingleton(InterfaceStub.Create<IDtoService>((method, args) => method.Name == "GetBaseItemDtos" ? ((IEnumerable<BaseItem>)args![0]!).Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList() : throw new NotSupportedException(method.Name)));
            services.AddSingleton(InterfaceStub.Create<ILiveTvManager>((method, args) => method.Name switch
            {
                "GetInternalChannels" => new QueryResult<BaseItem>(0, Accessible.Count, Accessible),
                "GetPrograms" => ReadPrograms((InternalItemsQuery)args![0]!),
                _ => throw new NotSupportedException(method.Name)
            }));
            Task<QueryResult<BaseItemDto>> ReadPrograms(InternalItemsQuery query)
            {
                Assert.Same(User, query.User);
                Assert.Equal([Channel.Id], query.ChannelIds);
                return Task.FromResult(new QueryResult<BaseItemDto>(0, 2,
                    [new BaseItemDto { Id = ProgramId, ChannelId = Channel.Id, Name = ProgramName, Overview = "Description", StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddDays(1) },
                     new BaseItemDto { Id = Guid.NewGuid(), ChannelId = Forbidden.Id, Name = "Forbidden program", StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddDays(1) }]));
            }
            Services = services.BuildServiceProvider();
            Groups = Services.GetRequiredService<GroupService>(); Provider = new GroupsMediaSourceProvider(Groups, Services);
        }
        public Video Video() => new() { Id = Guid.NewGuid(), ChannelId = Root, ExternalId = GroupsChannel.GetItemExternalId(Group, Channel.Id) };
        public void Authenticate(Guid userId) => Http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", userId.ToString())], "test"));
        public void Dispose() { Services.Dispose(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class StubBridge() : LiveTvStreamBridge(null!)
    {
        public readonly MediaSourceInfo Source = new() { Id = "native_source", Path = "http://localhost/native.ts" };
        public readonly ILiveStream Stream = InterfaceStub.Create<ILiveStream>((method, args) => method.Name == "Close" ? Task.CompletedTask : null);
        public int Reads; public string? Token; public List<ILiveStream>? Streams; public bool Closed;
        public override Task<IEnumerable<MediaSourceInfo>> GetMediaSources(LiveTvChannel channel, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult<IEnumerable<MediaSourceInfo>>([Source]); }
        public override Task<ILiveStream> OpenMediaSource(string token, List<ILiveStream> currentStreams, CancellationToken cancellationToken)
        { Token = token; Streams = currentStreams; ((InterfaceStub)Stream).Handler = (method, args) => { if (method.Name == "Close") { Closed = true; return Task.CompletedTask; } return null; }; return Task.FromResult(Stream); }
    }
}

public class InterfaceStub : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    { var proxy = Create<T, InterfaceStub>(); ((InterfaceStub)(object)proxy).Handler = handler; return proxy; }
}
