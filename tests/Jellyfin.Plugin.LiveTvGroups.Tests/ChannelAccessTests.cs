using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using Jellyfin.Plugin.LiveTvGroups.Web;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class ChannelAccessTests
{
    [Fact]
    public void SelectedUsersDenyOverlapNewUsersAndUnclassifiedChannels()
    {
        using var f = new AccessFixture(); f.Enable();
        Assert.True(f.Access.Allowed(f.Admin, f.Adult)); Assert.True(f.Access.Allowed(f.Alice, f.Adult));
        Assert.False(f.Access.Allowed(f.Bob, f.Adult)); Assert.False(f.Access.Allowed(AccessFixture.User("new"), f.Adult));
        Assert.True(f.Access.Allowed(f.Bob, f.News));
        f.Store.UpdateAdministration(d => { d.ChannelAccess.Rules.Add(new() { Name = "second", VisibleToAllUsers = true, DeniedUserIds = [f.Alice.Id], Channels = [ChannelAccessService.Reference(f.Adult)] }); return true; });
        Assert.False(f.Access.Allowed(f.Alice, f.Adult)); Assert.Equal(["second"], f.Access.Denials(f.Alice, f.Adult));
        f.Admin.SetPermission(PermissionKind.EnableLiveTvAccess, false); Assert.False(f.Access.Allowed(f.Admin, f.Adult));
    }
    [Fact]
    public void EveryoneExceptSelectedAndExistingJellyfinRights()
    {
        using var f = new AccessFixture(); f.Enable(); f.Store.UpdateAdministration(d => { var r = d.ChannelAccess.Rules[0]; r.VisibleToAllUsers = true; r.DeniedUserIds = [f.Bob.Id]; return true; });
        Assert.True(f.Access.Allowed(AccessFixture.User("new"), f.Adult)); Assert.False(f.Access.Allowed(f.Bob, f.Adult));
        f.Alice.SetPermission(PermissionKind.IsDisabled, true); Assert.False(f.Access.Allowed(f.Alice, f.Adult));
    }
    [Fact]
    public void SourceIdentitySurvivesRescanAndAmbiguousMatchesRemainRestricted()
    {
        using var f = new AccessFixture(); f.Enable(); var reference = ChannelAccessService.Reference(f.Adult);
        var wrong = AccessFixture.Channel("Adult", "9", "different", "a"); var moved = AccessFixture.Channel("renamed", "99", "iptv", "adult");
        f.Channels.Clear(); f.Channels.AddRange([wrong, moved]); Assert.True(f.Access.Allowed(f.Bob, wrong)); Assert.False(f.Access.Allowed(f.Bob, moved));
        var duplicate = AccessFixture.Channel("duplicate", "101", "iptv", "adult"); f.Channels.Add(duplicate);
        Assert.Equal(2, ChannelAccessService.Resolve(reference, f.Channels).Count); Assert.False(f.Access.Allowed(f.Bob, duplicate));
        f.Channels.Clear(); f.Channels.Add(wrong); Assert.Empty(ChannelAccessService.Resolve(reference, f.Channels));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenGroupReferencesArePreservedWhenEditingAndRestored(bool shared)
    {
        using var f = new AccessFixture(); f.Enable(); var group = new ChannelGroup() { Id = Guid.NewGuid(), Name = "mixed", Channels = [ChannelAccessService.Reference(f.News), ChannelAccessService.Reference(f.Adult)] };
        if (shared) f.Store.UpdateAdministration(d => { d.Mode = "shared"; d.Groups = [group]; return true; });
        else f.Store.Update(f.Bob.Id, d => { d.Groups = [group]; return true; });
        Assert.Equal(f.News.Id, Assert.Single(f.Groups.ResolveChannels(f.Bob, group, f.Groups.GetAccessibleChannels(f.Bob))).Id);
        // Ordinary personal users can edit visible content; the hidden reference remains stored.
        if (!shared) { Assert.IsType<NoContentResult>(f.GroupsApi(f.Bob).SetGroupChannels(group.Id, [])); Assert.Equal(f.Adult.Id, Assert.Single(f.Store.Get(f.Bob.Id).Groups[0].Channels).ItemId); }
        f.Store.UpdateAdministration(d => { d.ChannelAccess.Rules[0].AllowedUserIds.Add(f.Bob.Id); return true; });
        Assert.Contains(f.Groups.ResolveChannels(f.Bob, group, f.Groups.GetAccessibleChannels(f.Bob)), c => c.Id == f.Adult.Id);
    }
    [Fact]
    public void GroupDoesNotRemapMissingSourceToUnrelatedSameName()
    {
        using var f = new AccessFixture(); f.Enable(); var group = new ChannelGroup() { Id = Guid.NewGuid(), Name = "old", Channels = [ChannelAccessService.Reference(f.Adult)] };
        f.Store.Update(f.Bob.Id, d => { d.Groups = [group]; return true; }); f.Channels.Remove(f.Adult); f.Channels.Add(AccessFixture.Channel("Adult", "9", "other", "adult"));
        Assert.Empty(f.Groups.ResolveChannels(f.Bob, group, f.Groups.GetAccessibleChannels(f.Bob))); Assert.Single(f.Store.Get(f.Bob.Id).Groups[0].Channels);
    }
    [Fact]
    public async Task NativeTagsRecordingsRestartAndDisablePreserveUnrelatedRestrictions()
    {
        using var f = new AccessFixture(); f.Enable(); f.Adult.Tags = ["original"]; f.Bob.SetPreference(PreferenceKind.BlockedTags, ["parental"]);
        f.Program.Tags = ["genre"]; f.Recording.Tags = [ChannelAccessService.OriginPrefix + f.Adult.Id.ToString("N"), "recording"];
        await f.Bridge.EnsureAsync(CancellationToken.None);
        Assert.Contains("original", f.Adult.Tags); Assert.Contains(ChannelAccessService.UserTag(f.Bob.Id), f.Adult.Tags); Assert.DoesNotContain(ChannelAccessService.UserTag(f.Alice.Id), f.Adult.Tags);
        Assert.Contains(ChannelAccessService.UserTag(f.Bob.Id), f.Program.Tags); Assert.Contains(ChannelAccessService.UserTag(f.Bob.Id), f.Recording.Tags);
        Assert.Contains("parental", f.Bob.GetPreference(PreferenceKind.BlockedTags)); Assert.Single(f.Store.GetAdministration().ChannelAccess.Recordings);
        var restarted = new ChannelAccessService(new GroupStore(f.Directory), f.Services); Assert.False(restarted.ItemAllowed(f.Bob, f.Recording));
        f.Channels.Remove(f.Adult); f.Bridge.Invalidate(); await f.Bridge.EnsureAsync(CancellationToken.None); Assert.False(f.Access.ItemAllowed(f.Bob, f.Recording));
        Assert.Contains(ChannelAccessService.OriginPrefix + f.Adult.Id.ToString("N"), f.Recording.Tags);
        f.Store.UpdateAdministration(d => { d.ChannelAccess.Enabled = false; return true; }); await f.Bridge.EnsureAsync(CancellationToken.None);
        Assert.Equal(["parental"], f.Bob.GetPreference(PreferenceKind.BlockedTags)); Assert.Equal(["recording"], f.Recording.Tags); Assert.True(f.Access.ItemAllowed(f.Bob, f.Recording));
    }
    [Fact]
    public async Task NewUsersReceiveNativeRestrictionsAndNewChannelsRemainUnrestricted()
    {
        using var f = new AccessFixture(); f.Enable(); await f.Bridge.EnsureAsync(CancellationToken.None);
        var user = AccessFixture.User("new"); f.Users.Add(user); await f.Bridge.EnsureAsync(CancellationToken.None);
        Assert.Contains(ChannelAccessService.UserTag(user.Id), f.Adult.Tags); Assert.Contains(ChannelAccessService.UserTag(user.Id), user.GetPreference(PreferenceKind.BlockedTags));
        var channel = AccessFixture.Channel("new", "10", "iptv", "new"); f.Channels.Add(channel); f.Bridge.Invalidate(); await f.Bridge.EnsureAsync(CancellationToken.None); Assert.DoesNotContain(ChannelAccessService.UserTag(user.Id), channel.Tags);
    }
    [Fact]
    public async Task RevisionChangingDuringProjectionIsReconciledBeforeReturning()
    {
        using var f = new AccessFixture(); f.Enable(); f.OnUpdate = () => { f.OnUpdate = null; f.Store.UpdateAdministration(d => { d.ChannelAccess.Rules[0].AllowedUserIds.Add(f.Bob.Id); return true; }); };
        await f.Bridge.EnsureAsync(CancellationToken.None); Assert.DoesNotContain(ChannelAccessService.UserTag(f.Bob.Id), f.Adult.Tags);
    }
    [Fact]
    public void UnassignedOldRecordingsRemainUnchangedUntilManualAssignment()
    {
        using var f = new AccessFixture(); f.Enable(); Assert.True(f.Access.ItemAllowed(f.Bob, f.Recording));
        f.Store.UpdateAdministration(d => { d.ChannelAccess.Recordings.Add(new() { ItemId = f.Recording.Id, Path = f.Recording.Path, Channel = ChannelAccessService.Reference(f.Adult) }); return true; });
        Assert.False(f.Access.ItemAllowed(f.Bob, f.Recording));
    }
    [Fact]
    public void NativeAndGroupTokensRejectForeignUsersAndContradictorySources()
    {
        using var f = new AccessFixture(); var native = "provider_LiveTvChannel_" + f.Adult.Id.ToString("N") + "_source";
        Assert.Equal(f.Adult.Id, Assert.Single(ChannelAccessFilter.TokenItems(native, f.Bob.Id)));
        string Token(Guid owner, Guid source) => "provider_" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { UserId = owner, ExternalId = GroupsChannel.GetItemExternalId(Guid.NewGuid(), source), NativeToken = native })));
        Assert.Throws<UnauthorizedAccessException>(() => ChannelAccessFilter.TokenItems(Token(f.Alice.Id, f.Adult.Id), f.Bob.Id));
        Assert.Throws<UnauthorizedAccessException>(() => ChannelAccessFilter.TokenItems(Token(f.Bob.Id, f.News.Id), f.Bob.Id));
    }
    [Theory]
    [InlineData("Videos", "itemId")]
    [InlineData("DynamicHls", "id")]
    [InlineData("Image", "itemId")]
    [InlineData("Items", "itemId")]
    [InlineData("Library", "itemId")]
    [InlineData("Subtitle", "itemId")]
    [InlineData("Trickplay", "itemId")]
    [InlineData("MediaSegments", "itemId")]
    [InlineData("Subtitle", "routeItemId")]
    [InlineData("VideoAttachments", "videoId")]
    public async Task NativeDirectIdentifiersCannotBypassPolicy(string controller, string parameter)
    {
        using var f = new AccessFixture(); f.Enable(); var (result, ran) = await f.Invoke(f.Bob, controller, new() { [parameter] = f.Adult.Id }); Assert.IsType<NotFoundResult>(result); Assert.False(ran);
    }
    [Fact]
    public async Task NativeOpenTokenStreamMappingAndRemoteSessionCheckBothUsers()
    {
        using var f = new AccessFixture(); f.Enable();
        var (tokenResult, _) = await f.Invoke(f.Bob, "MediaInfo", new() { ["openToken"] = "provider_LiveTvChannel_" + f.Adult.Id.ToString("N") + "_source" }); Assert.IsType<NotFoundResult>(tokenResult);
        f.Revoker.AssociateStream(new(f.Alice.Id, f.Adult.Id, "tv", "play", null), "live", "source");
        var (streamResult, _) = await f.Invoke(f.Bob, "Videos", new() { ["itemId"] = f.News.Id, ["liveStreamId"] = "live" }); Assert.IsType<NotFoundResult>(streamResult);
        f.Sessions.Add(new(null!, NullLogger.Instance) { Id = "tv-session", UserId = f.Bob.Id }); var (remoteResult, _) = await f.Invoke(f.Alice, "Session", new() { ["itemIds"] = new[] { f.Adult.Id }, ["sessionId"] = "tv-session" }); Assert.IsType<NotFoundResult>(remoteResult);
    }
    [Fact]
    public async Task LegacySegmentsCannotForgeAnAllowedMovieIdentifier()
    {
        using var f = new AccessFixture(); f.Enable(); var (result, ran) = await f.Invoke(f.Alice, "HlsSegment", new() { ["itemId"] = f.News.Id, ["playlistId"] = "unowned-file" }); Assert.IsType<NotFoundResult>(result); Assert.False(ran);
    }
    [Fact]
    public async Task ExistingJellyfinRestrictionStillBlocksDirectPlayback()
    {
        using var f = new AccessFixture(); f.Enable(); f.BaseDenied.Add(f.News.Id); var (result, _) = await f.Invoke(f.Alice, "Videos", new() { ["itemId"] = f.News.Id }); Assert.IsType<NotFoundResult>(result);
    }
    [Fact]
    public async Task SourceUrlsAreProxiedWithoutMutatingSharedSources()
    {
        using var f = new AccessFixture(); f.Enable(); var source = new MediaSourceInfo() { Id = "native", Path = "https://provider.example/private.ts", SupportsDirectPlay = true };
        var response = new SourceResponse() { MediaSources = [source] }; var (result, ran) = await f.Invoke(f.Alice, "MediaInfo", new() { ["itemId"] = f.Adult.Id }, response);
        Assert.True(ran); Assert.IsType<OkObjectResult>(result); Assert.StartsWith("http://jellyfin.example/Videos/", response.MediaSources[0].Path, StringComparison.Ordinal); Assert.DoesNotContain("provider.example", response.MediaSources[0].Path, StringComparison.Ordinal); Assert.Equal("https://provider.example/private.ts", source.Path);
    }
    [Fact]
    public async Task RevocationStopsOnlyDeniedConsumersAndDoesNotCloseSharedTuner()
    {
        using var f = new AccessFixture(); f.Revoker.AssociateJob(new(f.Bob.Id, f.Adult.Id, "bob-device", "bob-play", "shared")); f.Revoker.AssociateJob(new(f.Alice.Id, f.Adult.Id, "alice-device", "alice-play", "shared"));
        f.Sessions.Add(new(null!, NullLogger.Instance) { Id = "bob-session", UserId = f.Bob.Id, FullNowPlayingItem = f.Adult }); f.Sessions.Add(new(null!, NullLogger.Instance) { Id = "alice-session", UserId = f.Alice.Id, FullNowPlayingItem = f.Adult });
        var http = new DefaultHttpContext(); var lifetime = new Lifetime(); http.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature>(lifetime); f.Revoker.Track(http, new(f.Bob.Id, f.Adult.Id, "bob-device", "bob-play", "shared"));
        f.Enable(); await f.Revoker.RevokeAsync(CancellationToken.None); Assert.True(lifetime.Aborted); Assert.Equal(["bob-session"], f.Stopped); Assert.Equal(["bob-device:bob-play"], f.Killed); Assert.Equal(0, f.ClosedStreams);
    }
    [Fact]
    public async Task AdministrationRejectsNormalUsersStaleRevisionsAndUnknownSources()
    {
        using var f = new AccessFixture(); Assert.IsType<ForbidResult>(f.Api(f.Bob).Get()); var stale = new ChannelAccessRequest() { Revision = -1 }; Assert.IsType<ConflictObjectResult>(await f.Api(f.Admin).Put(stale)); Assert.False(f.Access.Configuration.Enabled);
        var bad = new ChannelAccessRequest() { Rules = [new() { Name = "forged", Channels = [new() { ItemId = f.Adult.Id, ServiceName = "wrong", ExternalId = "wrong" }] }] }; Assert.IsType<BadRequestObjectResult>(f.Api(f.Admin).Preview(bad));
        Assert.IsType<BadRequestObjectResult>(f.Api(f.Admin).Preview(new() { Rules = [null!] }));
    }
    [Fact]
    public void PreviewRemovesOnlyOwnedRestrictionsFromACopy()
    {
        using var f = new AccessFixture(); f.Bob.SetPreference(PreferenceKind.BlockedTags, ["parental", ChannelAccessService.UserTag(f.Bob.Id)]);
        var clone = ChannelAccessController.BaseRightsUser(f.Bob); Assert.Equal(f.Bob.Id, clone.Id); Assert.Equal(["parental"], clone.GetPreference(PreferenceKind.BlockedTags)); Assert.Equal(2, f.Bob.GetPreference(PreferenceKind.BlockedTags).Length); Assert.True(clone.HasPermission(PermissionKind.EnableLiveTvAccess));
    }
    [Fact]
    public async Task NewDvrRecordingsCaptureSourceBeforeSaveWithoutNfoTags()
    {
        using var f = new AccessFixture(); f.Adult.ServiceName = "Emby"; f.Enable();
        f.ActiveRecording = new() { Path = f.Recording.Path, Timer = new() { ChannelId = f.Adult.ExternalId } };
        var provider = new ChannelAccessRecordingProvider(f.Access, f.Store, f.Services);
        Assert.Equal(ItemUpdateType.MetadataEdit, await provider.FetchAsync(f.Recording, null!, CancellationToken.None));
        Assert.False(f.Access.ItemAllowed(f.Bob, f.Recording)); Assert.Single(f.Access.Configuration.Recordings);
        f.ActiveRecording = null; f.Recording.Tags = []; Assert.False(f.Access.ItemAllowed(f.Bob, f.Recording));
        var restarted = new ChannelAccessService(new GroupStore(f.Directory), f.Services); Assert.False(restarted.ItemAllowed(f.Bob, f.Recording));
    }
    [Fact]
    public void CachedNativeEpgFoldersAlsoRespectSourceRules()
    {
        using var f = new AccessFixture(); f.Enable(); var group = Guid.NewGuid();
        var folder = new Folder() { ExternalId = "ltvepgchannel_" + group.ToString("N") + "_20260918_" + f.Adult.Id.ToString("N") };
        Assert.False(f.Access.ItemAllowed(f.Bob, folder)); Assert.True(f.Access.ItemAllowed(f.Alice, folder));
    }
    [Fact]
    public void ChannelSelectionRetainsHiddenPositionsAndEffectiveGuideCounts()
    {
        using var f = new AccessFixture(); f.Enable(); var group = new ChannelGroup() { Id = Guid.NewGuid(), Name = "mixed", Channels = [ChannelAccessService.Reference(f.News), ChannelAccessService.Reference(f.Adult), ChannelAccessService.Reference(f.News)] };
        f.Store.Update(f.Bob.Id, d => { d.Groups = [group]; return true; });
        Assert.IsType<NoContentResult>(f.GroupsApi(f.Bob).SetGroupChannels(group.Id, [f.News.Id]));
        Assert.Equal([f.News.Id, f.Adult.Id], f.Store.Get(f.Bob.Id).Groups[0].Channels.Select(r => r.ItemId));
        var guide = new GroupGuideService(f.Groups, f.Services); Assert.Equal(f.News.Id, Assert.Single(guide.ResolveScope(f.Bob, [group])).Id);
        var result = Assert.IsType<OkObjectResult>(f.GroupsApi(f.Bob).GetGroups().Result);
        Assert.Equal(1, Assert.Single((System.Collections.Generic.IEnumerable<GroupDto>)result.Value!).ChannelCount);
    }
    [Fact]
    public async Task MetadataProviderDoesNotTreatGroupedChannelVideosAsRecordings()
    {
        using var f = new AccessFixture(); f.Enable();
        var item = new Video { Id = Guid.NewGuid(), ExternalId = GroupsChannel.GetItemExternalId(Guid.NewGuid(), f.Adult.Id) };
        var provider = new ChannelAccessRecordingProvider(f.Access, f.Store, f.Services);
        Assert.Equal(ItemUpdateType.None, await provider.FetchAsync(item, null!, CancellationToken.None));
        Assert.Empty(f.Access.Configuration.Recordings);
    }
    [Fact]
    public async Task RemoteQueueAndResumeCheckControllingUserButStopRemainsAvailable()
    {
        using var f = new AccessFixture(); f.Enable();
        f.Sessions.Add(new(null!, NullLogger.Instance)
        {
            Id = "allowed-tv",
            UserId = f.Alice.Id,
            FullNowPlayingItem = f.News,
            NowPlayingQueue = [new QueueItem { Id = f.News.Id }, new QueueItem { Id = f.Adult.Id }]
        });
        var (next, ran) = await f.Invoke(f.Bob, "Session", new() { ["sessionId"] = "allowed-tv", ["command"] = PlaystateCommand.NextTrack });
        Assert.IsType<NotFoundResult>(next); Assert.False(ran);
        f.Sessions[0].FullNowPlayingItem = f.Adult;
        var (resume, _) = await f.Invoke(f.Bob, "Session", new() { ["sessionId"] = "allowed-tv", ["command"] = PlaystateCommand.Unpause });
        Assert.IsType<NotFoundResult>(resume);
        var (_, stopped) = await f.Invoke(f.Bob, "Session", new() { ["sessionId"] = "allowed-tv", ["command"] = PlaystateCommand.Stop });
        Assert.True(stopped);
    }
    [Fact]
    public async Task NativePageRacingPolicyChangeFailsClosedInsteadOfLeakingItems()
    {
        using var f = new AccessFixture();
        f.OnAction = () => { f.Enable(); f.Store.UpdateAdministration(d => { d.ChannelAccess.PolicyRevision++; return true; }); };
        // Start enabled with no rule, then introduce the denial while the native query executes.
        f.Store.UpdateAdministration(d => { d.ChannelAccess.Enabled = true; return true; });
        var (result, ran) = await f.Invoke(f.Bob, "Items", new(), new QueryResult<BaseItemDto>([new() { Id = f.Adult.Id }]));
        Assert.True(ran); Assert.Equal(503, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }
    [Fact]
    public async Task DisabledPolicyLeavesNativeCommandsAndListsUntouched()
    {
        using var f = new AccessFixture(); var response = new QueryResult<BaseItemDto>([new() { Id = f.Adult.Id }]);
        var (result, ran) = await f.Invoke(f.Bob, "Items", new(), response);
        Assert.True(ran); Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(f.Bob.GetPreference(PreferenceKind.BlockedTags));
    }
    [Fact]
    public async Task LegacyHlsFilesRetainOrdinaryMediaOwnerAndDenyForeignOrRevokedUsers()
    {
        using var f = new AccessFixture(); f.Enable();
        f.TranscodeJob = new TranscodingJob(NullLogger<TranscodingJob>.Instance) { Path = Path.Combine(Path.GetTempPath(), "ordinary-job.m3u8") };
        await f.Invoke(f.Bob, "Videos", new() { ["itemId"] = f.Recording.Id, ["playSessionId"] = "movie-play" });
        Assert.Equal(f.Recording.Id, f.Revoker.File("ordinary-job0")!.ItemId);
        var (_, allowed) = await f.Invoke(f.Bob, "HlsSegment", new() { ["itemId"] = f.News.Id, ["playlistId"] = "ordinary-job0" });
        Assert.True(allowed);
        f.Store.UpdateAdministration(d => { d.ChannelAccess.Recordings.Add(new() { ItemId = f.Recording.Id, Channel = ChannelAccessService.Reference(f.Adult) }); return true; });
        var (denied, ran) = await f.Invoke(f.Alice, "HlsSegment", new() { ["itemId"] = f.News.Id, ["playlistId"] = "ordinary-job0" });
        Assert.IsType<NotFoundResult>(denied); Assert.False(ran);
    }
    public class SourceResponse { public MediaSourceInfo[] MediaSources { get; set; } = []; }
    private class Lifetime : Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature { public bool Aborted; public CancellationToken RequestAborted { get; set; } public void Abort() => Aborted = true; }
}
internal sealed class AccessFixture : IDisposable
{
    public string Directory = Path.Combine(Path.GetTempPath(), "ltvg-acl-" + Guid.NewGuid().ToString("N"));
    public User Admin = User("admin", true), Alice = User("alice"), Bob = User("bob");
    public LiveTvChannel Adult = Channel("Adult", "9", "iptv", "adult"), News = Channel("News", "1", "iptv", "news");
    public InternalItemsQuery? LastProgramQuery; public LiveTvProgram Program; public Video Recording = new() { Id = Guid.NewGuid(), Name = "recording", Path = Path.Combine(Path.GetTempPath(), "recordings", "old.ts") };
    public List<User> Users; public List<LiveTvChannel> Channels; public List<SessionInfo> Sessions = []; public List<string> Stopped = [], Killed = []; public HashSet<Guid> BaseDenied = []; public ActiveRecordingInfo? ActiveRecording; public int ClosedStreams; public Action? OnUpdate; public Action? OnAction; public TranscodingJob? TranscodeJob;
    public ServiceProvider Services; public GroupStore Store; public ChannelAccessService Access; public ChannelAccessTagBridge Bridge; public ChannelAccessRevoker Revoker; public GroupService Groups; public IUserManager UserManager; public ILibraryManager Library;
    public AccessFixture()
    {
        Users = [Admin, Alice, Bob]; Channels = [Adult, News]; Program = new() { Id = Guid.NewGuid(), Name = "program", ChannelId = Adult.Id }; Store = new(Directory);
        UserManager = InterfaceStub.Create<IUserManager>((m, a) => m.Name switch { "GetUserById" => Users.FirstOrDefault(u => u.Id == (Guid)a![0]!), "GetUsers" => Users, "UpdateUserAsync" => Task.CompletedTask, _ => null });
        Library = InterfaceStub.Create<ILibraryManager>((m, a) => m.Name switch
        {
            "GetNewItemId" => Guid.Parse("77777777-7777-7777-7777-777777777777"),
            "GetItemById" => AllItems().FirstOrDefault(i => i.Id == (Guid)a![0]!),
            "FindByPath" => a![1] is true ? new Folder() { Id = Guid.Parse("88888888-8888-8888-8888-888888888888") } : AllItems().FirstOrDefault(i => i.Path == (string)a[0]!),
            "GetItemList" => Query((InternalItemsQuery)a![0]!),
            "UpdateItemAsync" => Update(),
            _ => null
        });
        var tv = InterfaceStub.Create<ILiveTvManager>((m, a) => m.Name switch
        {
            "GetInternalChannels" => NativeChannels((LiveTvChannelQuery)a![0]!),
            "GetPrograms" => Programs((InternalItemsQuery)a![0]!),
            _ => null
        });
        var recordings = InterfaceStub.Create<IRecordingsManager>((m, a) => m.Name switch { "GetRecordingFolders" => new[] { new VirtualFolderInfo() { Locations = [Path.Combine(Path.GetTempPath(), "recordings")] } }, "GetActiveRecordingInfo" => ActiveRecording, _ => null });
        var sessions = InterfaceStub.Create<ISessionManager>((m, a) => { if (m.Name == "get_Sessions") return Sessions; if (m.Name == "SendPlaystateCommand") { Stopped.Add((string)a![1]!); return Task.CompletedTask; } return null; });
        var transcode = InterfaceStub.Create<ITranscodeManager>((m, a) => { if (m.Name == "GetTranscodingJob") return TranscodeJob; if (m.Name == "KillTranscodingJobs") { Killed.Add(a![0] + ":" + a[1]); return Task.CompletedTask; } return null; });
        var media = InterfaceStub.Create<IMediaSourceManager>((m, a) => { if (m.Name == "CloseLiveStream") { ClosedStreams++; return Task.CompletedTask; } return null; });
        Services = new ServiceCollection().AddSingleton(Store).AddSingleton(UserManager).AddSingleton(Library).AddSingleton(tv).AddSingleton(recordings).AddSingleton(sessions).AddSingleton(transcode).AddSingleton(media)
            .AddLogging().AddSingleton<GroupService>().AddSingleton<ChannelAccessService>().AddSingleton<ChannelAccessTagBridge>().AddSingleton<ChannelAccessRevoker>().AddSingleton<PlaylistSyncService>().BuildServiceProvider();
        Access = Services.GetRequiredService<ChannelAccessService>(); Bridge = Services.GetRequiredService<ChannelAccessTagBridge>(); Revoker = Services.GetRequiredService<ChannelAccessRevoker>(); Groups = Services.GetRequiredService<GroupService>();
    }
    private Task<QueryResult<BaseItemDto>> Programs(InternalItemsQuery query)
    {
        LastProgramQuery = query;
        return Task.FromResult(new QueryResult<BaseItemDto>(query.StartIndex, 1,
            new[] { new BaseItemDto { Id = Program.Id, ChannelId = Program.ChannelId, Name = Program.Name } }));
    }
    private QueryResult<BaseItem> NativeChannels(LiveTvChannelQuery q)
    { var items = Channels.Where(c => q.UserId == Guid.Empty || !c.Tags.Contains(ChannelAccessService.UserTag(q.UserId))).Cast<BaseItem>().ToList(); return new(q.StartIndex, items.Count, items.Skip(q.StartIndex ?? 0).Take(q.Limit ?? int.MaxValue).ToArray()); }
    public IEnumerable<BaseItem> AllItems() => Channels.Cast<BaseItem>().Concat([Program, Recording]);
    private Task Update() { OnUpdate?.Invoke(); return Task.CompletedTask; }
    private IReadOnlyList<BaseItem> Query(InternalItemsQuery q)
    {
        if (q.ItemIds.Length > 0) return AllItems().Where(i => q.ItemIds.Contains(i.Id) && !BaseDenied.Contains(i.Id) && !(q.User?.GetPreference(PreferenceKind.BlockedTags).Intersect(i.Tags).Any() ?? false)).ToList();
        if (q.IncludeItemTypes.Contains(BaseItemKind.LiveTvProgram)) return [Program];
        if (q.IncludeItemTypes.Contains(BaseItemKind.LiveTvChannel)) return Channels.Where(c => !BaseDenied.Contains(c.Id)).Cast<BaseItem>().ToList();
        if (q.AncestorIds.Length > 0) return [Recording]; return [];
    }
    public void Enable() => Store.UpdateAdministration(d => { d.ChannelAccess.Enabled = true; d.ChannelAccess.Rules = [new() { Name = "Adult", AllowedUserIds = [Alice.Id], Channels = [ChannelAccessService.Reference(Adult)] }]; return true; });
    public DefaultHttpContext Http(User user) { var h = new DefaultHttpContext() { RequestServices = Services, User = new(new ClaimsIdentity([new Claim("Jellyfin-UserId", user.Id.ToString())], "test")) }; h.Request.Scheme = "http"; h.Request.Host = new("jellyfin.example"); h.Request.Headers.Authorization = "MediaBrowser Token=\"secret\""; return h; }
    public ChannelAccessController Api(User u) => new(Store, Access, Bridge, Revoker, UserManager, Library, Services.GetRequiredService<PlaylistSyncService>()) { ControllerContext = new() { HttpContext = Http(u) } };
    public GroupsController GroupsApi(User u) => new(Groups, UserManager, null!, new WebInjectionStatus(), Services.GetRequiredService<PlaylistSyncService>()) { ControllerContext = new() { HttpContext = Http(u) } };
    public async Task<(IActionResult? Result, bool Ran)> Invoke(User u, string controller, Dictionary<string, object?> args, object? response = null)
    {
        var action = new ActionContext(Http(u), new RouteData(), new ControllerActionDescriptor() { ControllerName = controller, ActionName = "Playback" }, new ModelStateDictionary());
        var context = new ActionExecutingContext(action, [], args, new object()); bool ran = false; action.HttpContext.Request.Method = "GET";
        var filter = new ChannelAccessFilter(Access, Bridge, Revoker, Services, NullLogger<ChannelAccessFilter>.Instance);
        ActionExecutedContext? executed = null; await filter.OnActionExecutionAsync(context, () => { ran = true; OnAction?.Invoke(); executed = new ActionExecutedContext(action, [], new object()) { Result = new OkObjectResult(response) }; return Task.FromResult(executed); });
        return (context.Result ?? executed?.Result, ran);
    }
    public static User User(string name, bool admin = false) { var u = new User(name, "auth", "reset"); u.SetPermission(PermissionKind.EnableLiveTvAccess, true); u.SetPermission(PermissionKind.EnableMediaPlayback, true); u.SetPermission(PermissionKind.IsAdministrator, admin); return u; }
    public static LiveTvChannel Channel(string name, string number, string service, string external) => new() { Id = Guid.NewGuid(), Name = name, Number = number, ServiceName = service, ExternalId = external };
    public void Dispose() { Services.Dispose(); if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
}
