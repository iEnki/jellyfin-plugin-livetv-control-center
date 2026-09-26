using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Data;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests
{
public class NativeGuideTests
{
    private static Guid Group(AccessFixture f, bool both = false)
    {
        var group = Guid.NewGuid();
        f.Store.Update(f.Alice.Id, d => { d.Groups.Add(new() { Id = group, Name = "News", Channels = both
            ? [GroupService.ToRef(f.Adult), GroupService.ToRef(f.News)] : [GroupService.ToRef(f.News)] }); return true; });
        return group;
    }
    private static DefaultHttpContext Http(AccessFixture f, string device = "tv", string client = "Jellyfin Android TV", string apiKey = "False")
    {
        var http = f.Http(f.Alice);
        http.User = new(new ClaimsIdentity([new("Jellyfin-UserId", f.Alice.Id.ToString()),
            new("Jellyfin-DeviceId", device), new("Jellyfin-Client", client), new("Jellyfin-IsApiKey", apiKey)], "test"));
        return http;
    }
    private static async Task<QueryResult<BaseItemDto>> Invoke(AccessFixture f, NativeGuideService service,
        DefaultHttpContext? http = null, int? start = 0, int? limit = 1, string actionName = "GetLiveTvChannels",
        Guid? queriedUser = null, Action<ActionExecutingContext>? duringAction = null, bool omitStart = false, bool omitLimit = false)
    {
        var action = new ActionContext(http ?? Http(f), new RouteData(), new ControllerActionDescriptor
        { ControllerTypeInfo = typeof(Jellyfin.Api.Controllers.LiveTvController).GetTypeInfo(), ActionName = actionName }, new ModelStateDictionary());
        var args = new Dictionary<string, object?> { ["startIndex"] = start, ["limit"] = limit, ["userId"] = queriedUser,
            ["addCurrentProgram"] = true, ["sortBy"] = "SortName" };
        if (omitStart) args.Remove("startIndex");
        if (omitLimit) args.Remove("limit");
        var context = new ActionExecutingContext(action, [], args, new object());
        ActionExecutedContext? executed = null;
        var filter = new NativeGuideChannelFilter(service, f.UserManager, NullLogger<NativeGuideChannelFilter>.Instance);
        await filter.OnActionExecutionAsync(context, () =>
        {
            duringAction?.Invoke(context);
            var original = new[] { new BaseItemDto { Id = f.Adult.Id, Name = "Adult" }, new BaseItemDto { Id = f.News.Id, Name = "News" } };
            var page = original.Skip((int?)args.GetValueOrDefault("startIndex") ?? 0).Take((int?)args.GetValueOrDefault("limit") ?? int.MaxValue).ToArray();
            executed = new ActionExecutedContext(action, [], new object()) { Result = new OkObjectResult(new QueryResult<BaseItemDto>((int?)args.GetValueOrDefault("startIndex"), 2, page)) };
            return Task.FromResult(executed);
        });
        if (omitStart) Assert.False(args.ContainsKey("startIndex")); else Assert.Equal(start, args["startIndex"]);
        if (omitLimit) Assert.False(args.ContainsKey("limit")); else Assert.Equal(limit, args["limit"]);
        Assert.Equal(true, args["addCurrentProgram"]); Assert.Equal("SortName", args["sortBy"]);
        return (QueryResult<BaseItemDto>)Assert.IsType<OkObjectResult>(executed!.Result).Value!;
    }

    [Fact]
    public async Task VisibleGroupUnionDeduplicatesOriginalChannelsAndExcludesHiddenGroups()
    {
        using var f=new AccessFixture();var service=new NativeGuideService(f.Store,f.Groups);
        var both=Group(f,both:true);Group(f);Group(f);
        service.SetVisibleGroups(f.Alice,"tv");
        var union=await Invoke(f,service,limit:null);Assert.Equal(2,union.TotalRecordCount);Assert.Equal(2,union.Items.Select(c=>c.Id).Distinct().Count());
        f.Store.Update(f.Alice.Id,d=>{d.Preferences.HiddenGroupIds=[both];return true;});
        var visible=await Invoke(f,service,limit:null);Assert.Equal(f.News.Id,Assert.Single(visible.Items).Id);Assert.Equal(1,visible.TotalRecordCount);
        Assert.True(service.GetSelection(f.Alice.Id,"tv")!.AllVisibleGroups);
    }

    [Fact]
    public async Task UnionModeIsDynamicAndResetRestoresUngroupedChannels()
    {
        using var f=new AccessFixture();var service=new NativeGuideService(f.Store,f.Groups);var news=Group(f);
        service.SetVisibleGroups(f.Alice,"tv");Assert.Equal(1,(await Invoke(f,service,limit:null)).TotalRecordCount);
        var both=Group(f,both:true);Assert.Equal(2,(await Invoke(f,service,limit:null)).TotalRecordCount);
        f.Store.Update(f.Alice.Id,d=>{d.Groups.RemoveAll(g=>g.Id==both);return true;});
        Assert.Equal(1,(await Invoke(f,service,limit:null)).TotalRecordCount);
        service.Clear(f.Alice.Id,"tv");Assert.Null(service.GetSelection(f.Alice.Id,"tv"));
        Assert.Equal(2,(await Invoke(f,service,limit:null)).TotalRecordCount); // Adult is outside the remaining News group.
        Assert.Equal(news,Assert.Single(f.Groups.GetGroups(f.Alice)).Id);
    }

    [Fact]
    public async Task UnionRevalidatesSharedGroupAccessAndCentralChannelRights()
    {
        using var f=new AccessFixture();f.Enable();var service=new NativeGuideService(f.Store,f.Groups);
        var news=Guid.NewGuid();var adult=Guid.NewGuid();
        f.Store.UpdateAdministration(d=>{d.Mode="shared";d.Groups=[new() {Id=news,Name="News",Channels=[GroupService.ToRef(f.News)]},new() {Id=adult,Name="Adult",Channels=[GroupService.ToRef(f.Adult)],DeniedUserIds=[f.Alice.Id]}];return true;});
        service.SetVisibleGroups(f.Alice,"tv");Assert.Equal(f.News.Id,Assert.Single((await Invoke(f,service,limit:null)).Items).Id);
        f.Store.UpdateAdministration(d=>{d.Groups[1].DeniedUserIds=[];return true;});Assert.Equal(2,(await Invoke(f,service,limit:null)).TotalRecordCount);
        f.Store.UpdateAdministration(d=>{d.ChannelAccess.Rules[0].AllowedUserIds=[];return true;});
        Assert.Equal(f.News.Id,Assert.Single((await Invoke(f,service,limit:null)).Items).Id);
    }

    [Fact]
    public async Task EmptyUnionInvalidatesOnlyItsDeviceAndFailedActivationPreservesSingleGroup()
    {
        using var f=new AccessFixture();var service=new NativeGuideService(f.Store,f.Groups);var group=Group(f);
        service.SetVisibleGroups(f.Alice,"tv");service.Set(f.Alice,"bedroom",group);
        f.Store.Update(f.Alice.Id,d=>{d.Preferences.HiddenGroupIds=[group];return true;});
        var normal=await Invoke(f,service);Assert.Equal(2,normal.TotalRecordCount);
        Assert.Null(service.GetSelection(f.Alice.Id,"tv"));Assert.Equal(group,service.Get(f.Alice.Id,"bedroom"));
        service.Set(f.Alice,"tv",group);Assert.Throws<NativeGuideGroupUnavailableException>(()=>service.SetVisibleGroups(f.Alice,"tv"));
        Assert.Equal(group,service.Get(f.Alice.Id,"tv")); // Explicit single-group activation still works for an otherwise hidden group.
    }

    [Fact]
    public void UnionPersistenceModeReplacementAndStaleCleanupAreIsolated()
    {
        using var f=new AccessFixture();var service=new NativeGuideService(f.Store,f.Groups);var group=Group(f);
        service.Set(f.Alice,"tv",group);service.SetVisibleGroups(f.Alice,"tv");service.Set(f.Alice,"bedroom",group);
        Assert.False(f.Store.Get(f.Alice.Id).NativeGuideScopes.ContainsKey("tv"));
        var reloaded=new NativeGuideService(new Jellyfin.Plugin.LiveTvGroups.Storage.GroupStore(f.Directory),f.Groups);
        Assert.Equal(new NativeGuideSelection(null,true),reloaded.GetSelection(f.Alice.Id,"tv"));
        Assert.Equal(group,reloaded.Get(f.Alice.Id,"bedroom"));Assert.Null(reloaded.GetSelection(f.Bob.Id,"tv"));
        service.Clear(f.Alice.Id,"tv",group);Assert.True(service.GetSelection(f.Alice.Id,"tv")!.AllVisibleGroups);
        var observed=service.GetSelection(f.Alice.Id,"tv")!;service.Set(f.Alice,"tv",group);
        service.ClearSelection(f.Alice.Id,"tv",observed);Assert.Equal(group,service.Get(f.Alice.Id,"tv"));
        Assert.DoesNotContain("tv",f.Store.Get(f.Alice.Id).NativeGuideVisibleGroupDevices);
        service.Clear(f.Alice.Id,"tv");Assert.Null(service.GetSelection(f.Alice.Id,"tv"));Assert.Equal(group,service.Get(f.Alice.Id,"bedroom"));
    }

    [Fact]
    public async Task ModeReplacementOrResetDuringCoreActionUsesTheCurrentSelection()
    {
        using var f=new AccessFixture();var service=new NativeGuideService(f.Store,f.Groups);var news=Group(f);Group(f,both:true);
        service.SetVisibleGroups(f.Alice,"tv");
        var single=await Invoke(f,service,limit:null,duringAction:_=>service.Set(f.Alice,"tv",news));
        Assert.Equal(f.News.Id,Assert.Single(single.Items).Id);Assert.Equal(1,single.TotalRecordCount);
        service.SetVisibleGroups(f.Alice,"tv");var reset=await Invoke(f,service,start:1,duringAction:_=>service.Clear(f.Alice.Id,"tv"));
        Assert.Equal(2,reset.TotalRecordCount);Assert.Equal(f.News.Id,Assert.Single(reset.Items).Id);
    }

    [Fact]
    public void ControllerAcceptsExplicitUnionReportsItAndResetsOfflineButRejectsMixedModes()
    {
        using var f=new AccessFixture();var service=new NativeGuideService(f.Store,f.Groups);var group=Group(f);
        var api=new NativeGuideController(service,null!,f.UserManager,NullLogger<NativeGuideController>.Instance) {ControllerContext=new() {HttpContext=Http(f)}};
        Assert.IsType<NoContentResult>(api.SetCurrent(new() {AllVisibleGroups=true}));
        var json=System.Text.Json.JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(api.GetCurrent()).Value);
        Assert.True(json.GetProperty("AllVisibleGroups").GetBoolean());Assert.True(json.GetProperty("Enabled").GetBoolean());Assert.Equal(System.Text.Json.JsonValueKind.Null,json.GetProperty("GroupId").ValueKind);
        Assert.IsType<BadRequestObjectResult>(api.SetCurrent(new() {GroupId=group,AllVisibleGroups=true}));
        Assert.IsType<BadRequestObjectResult>(api.SetCurrent(new()));Assert.True(service.GetSelection(f.Alice.Id,"tv")!.AllVisibleGroups);
        Assert.IsType<NoContentResult>(api.ClearDevice("tv"));Assert.Null(service.GetSelection(f.Alice.Id,"tv"));
        Assert.IsType<NoContentResult>(api.SetCurrent(new() {GroupId=group}));
        f.Store.Update(f.Alice.Id,d=>{d.Preferences.HiddenGroupIds=[group];return true;});
        Assert.IsType<NotFoundObjectResult>(api.SetCurrent(new() {AllVisibleGroups=true}));Assert.Equal(group,service.Get(f.Alice.Id,"tv"));
        api.ControllerContext.HttpContext=Http(f,apiKey:"True");Assert.IsType<UnauthorizedResult>(api.SetCurrent(new() {AllVisibleGroups=true}));
    }

    [Fact]
    public void LegacyScopeJsonCannotAccidentallyActivateAUnion()
    {
        var group=Guid.NewGuid();var old=System.Text.Json.JsonSerializer.Deserialize<Jellyfin.Plugin.LiveTvGroups.Model.UserGroups>("{\"NativeGuideScopes\":{\"tv\":\""+group+"\"}}")!;
        Assert.Equal(group,old.NativeGuideScopes["tv"]);Assert.Empty(old.NativeGuideVisibleGroupDevices);
    }

    [Fact]
    public async Task UnionScopeIsLimitedToOwnUserTvTargetsAndDoesNotFilterOtherClientsOrDevices()
    {
        using var f=new AccessFixture();var service=new NativeGuideService(f.Store,f.Groups);Group(f);
        var manager=f.Services.GetRequiredService<ISessionManager>();var players=new PlayerService(manager,f.UserManager,f.Groups);
        var api=new NativeGuideController(service,players,f.UserManager,NullLogger<NativeGuideController>.Instance) {ControllerContext=new() {HttpContext=Http(f,client:"Jellyfin Web")}};
        f.Alice.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers,true);
        var tv=new SessionInfo(manager,NullLogger.Instance) {Id="target",DeviceId="tv",Client="Jellyfin for Android TV",UserId=f.Bob.Id,SessionControllers=[InterfaceStub.Create<ISessionController>((m,a)=>m.Name=="get_IsSessionActive" ? true : false)]};
        f.Sessions.Add(tv);Assert.IsType<ConflictObjectResult>(api.SetDevice("tv",new() {AllVisibleGroups=true}));Assert.Null(service.GetSelection(f.Alice.Id,"tv"));
        tv.UserId=f.Alice.Id;Assert.IsType<NoContentResult>(api.SetDevice("tv",new() {AllVisibleGroups=true}));
        Assert.Equal(2,(await Invoke(f,service,Http(f,device:"other-tv"))).TotalRecordCount);
        Assert.Equal(2,(await Invoke(f,service,Http(f,client:"Jellyfin Web"))).TotalRecordCount);
        Assert.IsType<BadRequestObjectResult>(api.SetCurrent(new() {AllVisibleGroups=true}));
        f.Sessions.Clear();Assert.IsType<ConflictObjectResult>(api.SetDevice("tv",new() {AllVisibleGroups=true}));
        Assert.IsType<NoContentResult>(api.ClearDevice("tv"));Assert.Null(service.GetSelection(f.Alice.Id,"tv"));Assert.Null(service.GetSelection(f.Bob.Id,"tv"));
    }

    [Fact]
    public void ReadingAnInvalidVisibleUnionClearsOnlyTheObservedDevice()
    {
        using var f=new AccessFixture();var service=new NativeGuideService(f.Store,f.Groups);var group=Group(f);
        service.SetVisibleGroups(f.Alice,"tv");service.SetVisibleGroups(f.Alice,"bedroom");
        f.Store.Update(f.Alice.Id,d=>{d.Preferences.HiddenGroupIds=[group];return true;});
        var api=new NativeGuideController(service,null!,f.UserManager,NullLogger<NativeGuideController>.Instance) {ControllerContext=new() {HttpContext=Http(f)}};
        var json=System.Text.Json.JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(api.GetCurrent()).Value);
        Assert.False(json.GetProperty("Enabled").GetBoolean());Assert.False(json.GetProperty("AllVisibleGroups").GetBoolean());
        Assert.Null(service.GetSelection(f.Alice.Id,"tv"));Assert.True(service.GetSelection(f.Alice.Id,"bedroom")!.AllVisibleGroups);
    }

    [Theory]
    [InlineData(true,true)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task OmittedPaginationArgumentsAreFilteredAndRestoredOnSuccessAndCoreFailure(bool omitStart,bool omitLimit)
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups); service.Set(f.Alice,"tv",Group(f));
        var result=await Invoke(f,service,omitStart:omitStart,omitLimit:omitLimit);
        Assert.Equal(1,result.TotalRecordCount);Assert.Equal(f.News.Id,Assert.Single(result.Items).Id);
        ActionExecutingContext? captured=null;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Invoke(f,service,omitStart:omitStart,omitLimit:omitLimit,
            duringAction:c=>{captured=c;throw new InvalidOperationException("Core error");}));
        Assert.NotNull(captured);Assert.Equal(!omitStart,captured.ActionArguments.ContainsKey("startIndex"));
        Assert.Equal(!omitLimit,captured.ActionArguments.ContainsKey("limit"));
    }

    [Theory]
    [InlineData("Android TV")]
    [InlineData("Jellyfin for Android TV")]
    [InlineData("Jellyfin for Android TV (debug)")]
    [InlineData("Wholphin")]
    [InlineData("Wholphin (Debug)")]
    public async Task SupportedTvClientsActivateAndFilterNativeGuide(string client)
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups); var group = Group(f);
        var api = new NativeGuideController(service, null!, f.UserManager, NullLogger<NativeGuideController>.Instance)
        { ControllerContext = new() { HttpContext = Http(f, client: client) } };
        Assert.IsType<NoContentResult>(api.SetCurrent(new() { GroupId = group }));
        var result = await Invoke(f, service, Http(f, client: client));
        Assert.Equal(f.News.Id, Assert.Single(result.Items).Id); Assert.Equal(1, result.TotalRecordCount);
    }

    [Fact]
    public async Task FiltersBeforePaginationAndPreservesOriginalChannelIdsAndOptions()
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups);
        service.Set(f.Alice, "tv", Group(f));
        var result = await Invoke(f, service, duringAction: c => { Assert.Null(c.ActionArguments["startIndex"]); Assert.Null(c.ActionArguments["limit"]); });
        Assert.Equal(f.News.Id, Assert.Single(result.Items).Id); Assert.Equal(1, result.TotalRecordCount);
        var later = await Invoke(f, service, start: 1); Assert.Empty(later.Items); Assert.Equal(1, later.TotalRecordCount); Assert.Equal(1, later.StartIndex);
    }

    [Theory]
    [InlineData("other-tv", "Jellyfin Android TV", "False", "GetLiveTvChannels")]
    [InlineData("tv", "Jellyfin Web", "False", "GetLiveTvChannels")]
    [InlineData("tv", "Fire TV", "False", "GetLiveTvChannels")]
    [InlineData("tv", "Wholphin Web", "False", "GetLiveTvChannels")]
    [InlineData("tv", "Wholphin", "True", "GetLiveTvChannels")]
    [InlineData("tv", "Wholphin", "False", "GetLiveTvPrograms")]
    [InlineData("tv", "Jellyfin Android TV", "True", "GetLiveTvChannels")]
    [InlineData("tv", "Jellyfin Android TV", "", "GetLiveTvChannels")]
    [InlineData("", "Jellyfin Android TV", "False", "GetLiveTvChannels")]
    [InlineData("tv", "Jellyfin Android TV", "False", "GetLiveTvPrograms")]
    public async Task UnscopedOtherClientsApiKeysAndProgramsRemainUnmodified(string device, string client, string apiKey, string action)
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups); service.Set(f.Alice, "tv", Group(f));
        var result = await Invoke(f, service, Http(f, device, client, apiKey), actionName: action);
        Assert.Equal(f.Adult.Id, Assert.Single(result.Items).Id); Assert.Equal(2, result.TotalRecordCount);
    }

    [Fact]
    public async Task DifferentQueriedUserAndMissingAuthenticationBypassScope()
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups); service.Set(f.Alice, "tv", Group(f));
        Assert.Equal(2, (await Invoke(f, service, queriedUser: f.Bob.Id)).TotalRecordCount);
        var http = Http(f); http.User = new(new ClaimsIdentity(http.User.Claims));
        Assert.Equal(2, (await Invoke(f, service, http)).TotalRecordCount);
        http.User = new(new ClaimsIdentity(http.User.Claims.Where(c => c.Type != "Jellyfin-UserId"), "test"));
        Assert.Equal(2, (await Invoke(f, service, http)).TotalRecordCount);
    }

    [Fact]
    public async Task DeletedOrEmptyGroupRestoresNormalLiveTvAndClearsScope()
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups); var group = Group(f);
        service.Set(f.Alice, "tv", group); f.Store.Update(f.Alice.Id, d => { d.Groups.Clear(); return true; });
        Assert.Equal(2, (await Invoke(f, service)).TotalRecordCount); Assert.Null(service.Get(f.Alice.Id, "tv"));
        group = Group(f); service.Set(f.Alice, "tv", group); f.Channels.Remove(f.News);
        Assert.Equal(2, (await Invoke(f, service)).TotalRecordCount); Assert.Null(service.Get(f.Alice.Id, "tv"));
    }

    [Fact]
    public async Task SharedGroupRevocationAndChannelPoliciesAreRevalidated()
    {
        using var f = new AccessFixture(); f.Enable(); var service = new NativeGuideService(f.Store, f.Groups);
        var id = Guid.NewGuid(); f.Store.UpdateAdministration(d => { d.Mode = "shared"; d.Groups = [new() { Id = id, Name = "All", Channels = [GroupService.ToRef(f.Adult), GroupService.ToRef(f.News)] }]; return true; });
        service.Set(f.Alice, "tv", id); Assert.Equal(2, (await Invoke(f, service, limit: null)).Items.Count);
        f.Store.UpdateAdministration(d => { d.Groups[0].DeniedUserIds.Add(f.Alice.Id); return true; });
        Assert.Equal(2, (await Invoke(f, service)).TotalRecordCount); Assert.Null(service.Get(f.Alice.Id, "tv"));
        f.Store.UpdateAdministration(d => { d.Groups[0].DeniedUserIds.Clear(); d.ChannelAccess.Rules[0].AllowedUserIds.Clear(); return true; });
        service.Set(f.Alice, "tv", id); var result = await Invoke(f, service); Assert.Equal(f.News.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task ResetOrDeletionDuringActionRestoresOriginalPageWithoutSecondAction()
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups); service.Set(f.Alice, "tv", Group(f));
        var calls = 0; var result = await Invoke(f, service, start: 1, duringAction: _ => { calls++; service.Clear(f.Alice.Id, "tv"); });
        Assert.Equal(1, calls); Assert.Equal(2, result.TotalRecordCount); Assert.Equal(f.News.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task RightsResolutionFailureFailsOpenAndKeepsPagination()
    {
        using var f = new AccessFixture(); var group = Group(f); var good = new NativeGuideService(f.Store, f.Groups); good.Set(f.Alice, "tv", group);
        using var unavailable = new ServiceCollection().BuildServiceProvider(); var broken = new NativeGuideService(f.Store, new GroupService(f.Store, unavailable));
        var result = await Invoke(f, broken, start: 1); Assert.Equal(f.News.Id, Assert.Single(result.Items).Id); Assert.Equal(2, result.TotalRecordCount);
    }

    [Fact]
    public async Task ResolutionFailureAfterActionRestoresOrdinaryPage()
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups); service.Set(f.Alice, "tv", Group(f));
        var inventory = f.Channels;
        try
        {
            var result = await Invoke(f, service, start: 1, duringAction: _ => f.Channels = null!);
            Assert.Equal(2, result.TotalRecordCount); Assert.Equal(f.News.Id, Assert.Single(result.Items).Id);
        }
        finally { f.Channels = inventory; }
    }

    [Fact]
    public async Task CoreExceptionsAreNotRetriedAndArgumentsAreRestored()
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups);
        foreach (var scoped in new[] { false, true })
        {
            if (scoped) service.Set(f.Alice, "tv", Group(f));
            var calls = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(f, service, duringAction: _ => { calls++; throw new InvalidOperationException("Core error"); }));
            Assert.Equal(1, calls);
        }
    }

    [Fact]
    public void PersistenceIsolationResetAndCompareBeforeCleanup()
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups); var group = Group(f); var other = Group(f);
        service.Set(f.Alice, "tv", group); service.Set(f.Alice, "bedroom", other);
        var reloaded = new NativeGuideService(new GroupStore(f.Directory), f.Groups);
        Assert.Equal(group, reloaded.Get(f.Alice.Id, "tv")); Assert.Null(reloaded.Get(f.Bob.Id, "tv"));
        service.Set(f.Alice, "tv", other); service.Clear(f.Alice.Id, "tv", group); Assert.Equal(other, service.Get(f.Alice.Id, "tv"));
        service.Clear(f.Alice.Id, "tv"); Assert.Null(service.Get(f.Alice.Id, "tv")); Assert.Equal(other, service.Get(f.Alice.Id, "bedroom"));
    }

    [Fact]
    public void ControllerRejectsKeysUnknownGroupsNonTvAndCrossUserDevicesAndResetsOffline()
    {
        using var f = new AccessFixture(); var service = new NativeGuideService(f.Store, f.Groups);
        var players = new PlayerService(f.Services.GetRequiredService<ISessionManager>(), f.UserManager, f.Groups);
        var api = new NativeGuideController(service, players, f.UserManager, NullLogger<NativeGuideController>.Instance) { ControllerContext = new() { HttpContext = Http(f) } };
        var group = Group(f);
        Assert.IsType<NoContentResult>(api.SetCurrent(new() { GroupId = group }));
        Assert.IsType<NotFoundObjectResult>(api.SetCurrent(new() { GroupId = Guid.NewGuid() }));
        api.ControllerContext.HttpContext = Http(f, client: "Jellyfin Web");
        Assert.IsType<BadRequestObjectResult>(api.SetCurrent(new() { GroupId = group }));
        Assert.IsType<ConflictObjectResult>(api.SetDevice("guessed", new() { GroupId = group }));
        f.Sessions.Add(new(f.Services.GetRequiredService<ISessionManager>(), NullLogger.Instance) { Id = "target", DeviceId = "tv", Client = "Jellyfin Android TV", UserId = f.Bob.Id });
        f.Alice.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, true);
        Assert.IsType<ConflictObjectResult>(api.SetDevice("tv", new() { GroupId = group }));
        f.Sessions.Clear();
        f.Sessions.Add(new(f.Services.GetRequiredService<ISessionManager>(), NullLogger.Instance)
        {
            Id = "own-tv", DeviceId = "tv", Client = "Jellyfin Android TV", UserId = f.Alice.Id,
            SessionControllers = [InterfaceStub.Create<ISessionController>((m, a) => m.Name == "get_IsSessionActive" ? true : null)]
        });
        Assert.IsType<NoContentResult>(api.SetDevice("tv", new() { GroupId = group }));
        f.Sessions.Clear();
        Assert.IsType<NoContentResult>(api.ClearDevice("tv")); Assert.Null(service.Get(f.Alice.Id, "tv"));
        api.ControllerContext.HttpContext = Http(f, apiKey: "True"); Assert.IsType<UnauthorizedResult>(api.SetCurrent(new() { GroupId = group }));
    }
}
}

// Internal marker only: production matching is qualified to the real Jellyfin controller, not a route string.
namespace Jellyfin.Api.Controllers { internal class LiveTvController { } }
