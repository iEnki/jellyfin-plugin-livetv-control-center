using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests
{
public class NativeGuideActionTests
{
    [Theory]
    [InlineData("Android TV")]
    [InlineData("Jellyfin Android TV")]
    [InlineData("Jellyfin for Android TV")]
    public async Task ExplicitFolderVisitSetsOwnDeviceAndNavigatesRealLiveTv(string client)
    {
        using var f = new Fixture(client); var result = await f.Actions.OpenAsync(f.Http, f.ActionFolder.Id, null);
        Assert.NotNull(result); Assert.True(result.NavigationCommandSent);
        Assert.Equal(f.GroupId, f.Scopes.Get(f.User.Id, "tv"));
        Assert.Null(f.Scopes.Get(f.User.Id, "other-tv")); Assert.Null(f.Scopes.Get(f.Access.Bob.Id, "tv"));
        Assert.Equal(f.Session.Id, f.Controlling); Assert.Equal(f.Session.Id, f.Target);
        Assert.Equal(GeneralCommandType.DisplayContent, f.Command!.Name);
        Assert.Equal(f.View.Id.ToString("N"), f.Command.Arguments["ItemId"]);
        Assert.Equal("UserView", f.Command.Arguments["ItemType"]);
        Assert.Equal(f.User.Id, f.Command.ControllingUserId);
    }

    [Fact]
    public async Task RepeatedCachedReadsDebounceButResetAndNewGroupDoNot()
    {
        using var f = new Fixture();
        await f.Actions.OpenAsync(f.Http, f.ActionFolder.Id, f.PluginId);
        await f.Actions.OpenAsync(f.Http, f.ActionFolder.Id, f.PluginId);
        Assert.Equal(1, f.Sends);
        f.ActionFolder.ExternalId = NativeGuideActionRoute.AllChannelsId;
        await f.Actions.OpenAsync(f.Http, f.ActionFolder.Id, f.PluginId);
        Assert.Null(f.Scopes.Get(f.User.Id, "tv")); Assert.Equal(2, f.Sends);
        f.ActionFolder.ExternalId = NativeGuideActionRoute.GroupId(f.GroupId);
        await f.Actions.OpenAsync(f.Http, f.ActionFolder.Id, f.PluginId);
        var other = Guid.NewGuid(); f.Access.Store.Update(f.User.Id, d => { d.Groups.Add(new() { Id=other,Name="Other",Channels=[GroupService.ToRef(f.Access.Adult)] }); return true; });
        f.ActionFolder.ExternalId = NativeGuideActionRoute.GroupId(other);
        await f.Actions.OpenAsync(f.Http, f.ActionFolder.Id, f.PluginId);
        Assert.Equal(other, f.Scopes.Get(f.User.Id, "tv")); Assert.Equal(4, f.Sends);
    }

    [Theory]
    [InlineData("playback")]
    [InlineData("transport")]
    [InlineData("unsupported")]
    [InlineData("view")]
    [InlineData("send-failure")]
    public async Task ScopeRemainsUsableWhenAutomaticNavigationCannotRun(string reason)
    {
        using var f = new Fixture();
        if (reason=="playback") f.Session.NowPlayingItem=new() { Id=Guid.NewGuid() };
        if (reason=="transport") f.Session.SessionControllers=[];
        if (reason=="unsupported") f.Session.Capabilities=new();
        if (reason=="view") f.View.ViewType=CollectionType.movies;
        if (reason=="send-failure") f.SendFailure=true;
        var result=await f.Actions.OpenAsync(f.Http,f.ActionFolder.Id,null);
        Assert.NotNull(result); Assert.False(result.NavigationCommandSent);
        Assert.Equal(f.GroupId,f.Scopes.Get(f.User.Id,"tv"));
        Assert.Equal(reason=="send-failure" ? 1 : 0,f.Sends);
    }

    [Theory]
    [InlineData("api-key")]
    [InlineData("no-token")]
    [InlineData("device")]
    [InlineData("user")]
    [InlineData("other-client")]
    [InlineData("anonymous")]
    [InlineData("folder-source")]
    [InlineData("ordinary-folder")]
    [InlineData("channel-visibility")]
    [InlineData("deleted-group")]
    [InlineData("disabled")]
    public async Task InvalidIdentityRoutesAndRightsNeverSelectOrSend(string reason)
    {
        using var f=new Fixture();
        switch(reason)
        {
            case "api-key": f.Auth.IsApiKey=true; break;
            case "no-token": f.Auth.Token=null!; break;
            case "device": f.Session.DeviceId="other-device"; break;
            case "user": f.Session.UserId=f.Access.Bob.Id; break;
            case "other-client": f.SetPrincipal("Fake Fire TV"); break;
            case "anonymous": f.Http.User=new(); break;
            case "folder-source": f.ActionFolder.ChannelId=Guid.NewGuid(); break;
            case "ordinary-folder": f.ActionFolder.ExternalId=GroupsChannel.GetFolderExternalId(f.GroupId); break;
            case "channel-visibility": f.ChannelVisible=false; break;
            case "deleted-group": f.Access.Store.Update(f.User.Id,d=>{d.Groups.Clear();return true;}); break;
            case "disabled": f.User.SetPermission(PermissionKind.IsDisabled,true); break;
        }
        Assert.Null(await f.Actions.OpenAsync(f.Http,f.ActionFolder.Id,null));
        Assert.Null(f.Scopes.Get(f.User.Id,"tv")); Assert.Equal(0,f.Sends);
    }

    [Fact]
    public async Task ForeignChannelAndRevokedSharedGroupNeverActivate()
    {
        using var f=new Fixture();
        Assert.Null(await f.Actions.OpenAsync(f.Http,f.ActionFolder.Id,Guid.NewGuid()));
        f.Access.Store.UpdateAdministration(d=>{d.Mode="shared";d.Groups=f.Access.Store.Get(f.User.Id).Groups;d.Groups[0].DeniedUserIds=[f.User.Id];return true;});
        Assert.Null(await f.Actions.OpenAsync(f.Http,f.ActionFolder.Id,null)); Assert.Equal(0,f.Sends);
    }

    [Fact]
    public async Task ProviderAndGroupBrowsingNeverActivateScopes()
    {
        using var f=new Fixture(); var provider=new GroupsChannel(f.Access.Groups,f.Services,NullLogger<GroupsChannel>.Instance);
        await provider.GetChannelItems(new() {UserId=f.User.Id},CancellationToken.None);
        var group=await provider.GetChannelItems(new() {UserId=f.User.Id,FolderId=GroupsChannel.GetFolderExternalId(f.GroupId)},CancellationToken.None);
        Assert.Contains(group.Items,i=>i.Id==NativeGuideActionRoute.GroupId(f.GroupId));
        Assert.Contains(group.Items,i=>AppGuideService.Handles(i.Id));
        var help=await provider.GetChannelItems(new() {UserId=f.User.Id,FolderId=NativeGuideActionRoute.GroupId(f.GroupId)},CancellationToken.None);
        Assert.Single(help.Items); Assert.Null(f.Scopes.Get(f.User.Id,"tv")); Assert.Equal(0,f.Sends);
    }

    [Theory]
    [InlineData("Items","GetItems")]
    [InlineData("Items","GetItemsByUserIdLegacy")]
    [InlineData("Channels","GetChannelItems")]
    public async Task PerRequestFilterActivatesSuccessfulNativeReads(string controller,string action)
    {
        using var f=new Fixture(); var response=await Invoke(f,controller,action);
        Assert.Equal(f.GroupId,f.Scopes.Get(f.User.Id,"tv")); Assert.Equal(1,f.Sends);
        Assert.Contains("News",Assert.Single(response.Items).Name,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("recursive")]
    [InlineData("search")]
    [InlineData("latest")]
    [InlineData("foreign-user")]
    [InlineData("wrong-action")]
    [InlineData("core-failure")]
    [InlineData("core-exception")]
    public async Task BackgroundSearchAndFailedCoreReadsNeverActivate(string reason)
    {
        using var f=new Fixture();
        if(reason=="core-exception") await Assert.ThrowsAsync<InvalidOperationException>(()=>Invoke(f,"Items","GetItems",reason));
        else await Invoke(f,"Items",reason=="wrong-action" ? "GetItem" : "GetItems",reason);
        Assert.Null(f.Scopes.Get(f.User.Id,"tv")); Assert.Equal(0,f.Sends);
    }

    private static async Task<QueryResult<BaseItemDto>> Invoke(Fixture f,string controller,string action,string? reason=null)
    {
        var descriptor=new ControllerActionDescriptor { ControllerTypeInfo=(controller=="Items" ? typeof(Jellyfin.Api.Controllers.ItemsController) : typeof(Jellyfin.Api.Controllers.ChannelsController)).GetTypeInfo(),ActionName=action };
        var ctx=new ActionContext(f.Http,new RouteData(),descriptor,new ModelStateDictionary());
        var args=new Dictionary<string,object?> { [controller=="Items" ? "parentId" : "folderId"]=f.ActionFolder.Id,["channelId"]=f.PluginId,["userId"]=null };
        if(reason=="recursive") args["recursive"]=true;
        if(reason=="search") args["searchTerm"]="guide";
        if(reason=="latest") args["filters"]=new[] {MediaBrowser.Model.Querying.ItemFilter.IsUnplayed};
        if(reason=="foreign-user") args["userId"]=f.Access.Bob.Id;
        var response=new QueryResult<BaseItemDto>([new() {Id=Guid.NewGuid(),Name="Help"}]); var calls=0;
        var filter=new NativeGuideActionFilter(f.Actions,NullLogger<NativeGuideActionFilter>.Instance);
        await filter.OnActionExecutionAsync(new(ctx,[],args,new object()),()=>
        {
            calls++; if(reason=="core-exception") throw new InvalidOperationException("Core failure");
            return Task.FromResult(new ActionExecutedContext(ctx,[],new object()) {Result=reason=="core-failure" ? new StatusCodeResult(503) : new OkObjectResult(response)});
        });
        Assert.Equal(1,calls); return response;
    }

    [NativeApiFact]
    public async Task RealJellyfinItemsAndChannelsRoutesSelectResetAndFilterTheNativeGuide()
    {
        var path=Environment.GetEnvironmentVariable("JELLYFIN_NATIVE_API")!;
        Assembly? Resolve(AssemblyLoadContext context,AssemblyName name)
        {var file=Path.Combine(Path.GetDirectoryName(path)!,name.Name+".dll");return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;}
        AssemblyLoadContext.Default.Resolving+=Resolve;
        try
        {
            var api=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)); using var f=new Fixture();
            using var host=await new HostBuilder().ConfigureWebHost(web=>web.UseTestServer().ConfigureServices(s=>
            {

                new PluginServiceRegistrator().RegisterServices(s,InterfaceStub.Create<IServerApplicationHost>((m,a)=>null));
                s.AddSingleton(f.Access.Store).AddSingleton(f.Access.Groups).AddSingleton(f.Access.UserManager).AddSingleton(f.Library).AddSingleton(f.Manager).AddSingleton(f.Channels).AddSingleton(f.Tv).AddSingleton(f.AuthContext);
                s.AddSingleton(f.Access.Access).AddSingleton(f.Access.Bridge).AddSingleton(f.Access.Revoker);
                s.AddSingleton(InterfaceStub.Create<IDtoService>((m,a)=> m.Name=="GetBaseItemDtos" ? ((IEnumerable<BaseItem>)a![0]!).Select(i=>new BaseItemDto {Id=i.Id,Name=i.Name}).ToArray() : null));
                s.AddControllers().AddApplicationPart(api).AddJsonOptions(o=>o.JsonSerializerOptions.PropertyNamingPolicy=null).ConfigureApplicationPartManager(m=>m.FeatureProviders.Add(new NativeControllers()));
                s.AddSingleton<IControllerActivator,Activator>();

                s.AddAuthorization(o=>{o.AddPolicy("LiveTvAccess",p=>p.RequireAssertion(_=>true));o.DefaultPolicy=new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAssertion(_=>true).Build();});
            }).Configure(app=>
            {
                app.Use(async(http,next)=>{http.User=f.Http.User;await next();}); app.UseRouting(); app.UseAuthorization(); app.UseEndpoints(e=>e.MapControllers());
            })).StartAsync();
            var client=host.GetTestClient(); var scopes=host.Services.GetRequiredService<NativeGuideService>();
            foreach(var route in new[] {"/Items?parentId="+f.ActionFolder.Id,"/Users/"+f.User.Id+"/Items?parentId="+f.ActionFolder.Id,"/Channels/"+f.PluginId+"/Items?folderId="+f.ActionFolder.Id})
            {
                f.ActionFolder.ExternalId=NativeGuideActionRoute.GroupId(f.GroupId); scopes.Clear(f.User.Id,"tv");
                var read=await client.GetAsync(route); Assert.Equal(HttpStatusCode.OK,read.StatusCode);
                Assert.Equal(f.GroupId,scopes.Get(f.User.Id,"tv"));
                foreach(var guideRoute in new[] {"/LiveTv/Channels","/LiveTv/Channels?limit=1","/LiveTv/Channels?startIndex=0","/LiveTv/Channels?startIndex=0&limit=1"})
                {
                    using var guide=System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(guideRoute));
                    Assert.Equal(1,guide.RootElement.GetProperty("TotalRecordCount").GetInt32());
                    Assert.Equal(f.Access.News.Id,guide.RootElement.GetProperty("Items")[0].GetProperty("Id").GetGuid());
                }
                f.ActionFolder.ExternalId=NativeGuideActionRoute.AllChannelsId;
                Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(route)).StatusCode); Assert.Null(scopes.Get(f.User.Id,"tv"));
            }
            Assert.Equal(6,f.Sends);
        }
        finally {AssemblyLoadContext.Default.Resolving-=Resolve;}
    }

    private sealed class NativeControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts,ControllerFeature feature)
        {foreach(var t in feature.Controllers.Where(t=>t.Namespace!="Jellyfin.Api.Controllers" || t.Name is not ("ItemsController" or "ChannelsController" or "LiveTvController")).ToArray())feature.Controllers.Remove(t);}
    }
    private sealed class Activator : IControllerActivator
    {
        public object Create(ControllerContext context)=>Make(context.ActionDescriptor.ControllerTypeInfo.AsType(),context.HttpContext.RequestServices);
        public void Release(ControllerContext context,object controller) {}
        private static object Make(Type type,IServiceProvider services)
        {
            if(services.GetService(type) is {} value)return value;
            if(type.IsInterface)return typeof(InterfaceStub).GetMethod("Create",BindingFlags.Public|BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null,[new Func<MethodInfo,object?[]?,object?>((m,a)=>null)])!;
            var ctor=type.GetConstructors().OrderBy(c=>c.GetParameters().Length).First();return ctor.Invoke(ctor.GetParameters().Select(p=>p.HasDefaultValue ? p.DefaultValue : Make(p.ParameterType,services)).ToArray());
        }
    }
    private sealed class ActionFolder : Folder
    {
        public override bool IsVisible(User user,bool skipAllowedTagsCheck=false)=>true;
        protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)=>new([new Folder {Id=Guid.NewGuid(),Name="Help"}]);
    }
    private sealed class Fixture : IDisposable
    {
        public AccessFixture Access=new(); public User User=>Access.Alice;
        public Guid GroupId=Guid.NewGuid(),PluginId=Guid.Parse("77777777-7777-7777-7777-777777777777");
        public ActionFolder ActionFolder; public UserView View=new() {Id=Guid.NewGuid(),Name="Live TV",ViewType=CollectionType.livetv};
        public DefaultHttpContext Http=new(); public SessionInfo Session; public AuthorizationInfo Auth;
        public ILibraryManager Library; public ISessionManager Manager; public IChannelManager Channels; public ILiveTvManager Tv; public IAuthorizationContext AuthContext;
        public ServiceProvider Services; public NativeGuideService Scopes; public NativeGuideActionService Actions;
        public bool ChannelVisible=true,SendFailure; public int Sends; public GeneralCommand? Command; public string? Controlling,Target;
        public Fixture(string client="Jellyfin for Android TV")
        {
            Access.Store.Update(User.Id,d=>{d.Groups.Add(new() {Id=GroupId,Name="News",Channels=[GroupService.ToRef(Access.News)]});return true;});
            ActionFolder=new() {Id=Guid.NewGuid(),Name="Native guide",ExternalId=NativeGuideActionRoute.GroupId(GroupId),ChannelId=PluginId};
            Library=InterfaceStub.Create<ILibraryManager>((m,a)=>m.Name switch {"GetNewItemId"=>PluginId,"GetParentItem"=>ActionFolder,"GetItemById"=>(Guid)a![0]! == ActionFolder.Id ? ActionFolder : Access.AllItems().FirstOrDefault(i=>i.Id==(Guid)a[0]!),_=>null});
            Manager=InterfaceStub.Create<ISessionManager>((m,a)=>
            {
                if(m.Name=="get_Sessions")return new[] {Session!};
                if(m.Name=="GetSessionByAuthenticationToken")return Task.FromResult(Session!);
                if(m.Name=="SendGeneralCommand") {Sends++;Controlling=(string)a![0]!;Target=(string)a[1]!;Command=(GeneralCommand)a[2]!; if(SendFailure)throw new IOException("Test send failure");return Task.CompletedTask;}
                return null;
            });
            Session=new(Manager,NullLogger.Instance) {Id="tv-session",DeviceId="tv",UserId=User.Id,Client=client,Capabilities=new() {SupportedCommands=[GeneralCommandType.DisplayContent]},SessionControllers=[InterfaceStub.Create<ISessionController>((m,a)=>m.Name=="get_IsSessionActive" ? true : false)]};
            Auth=new() {User=User,Token="test-token",DeviceId="tv",Client=client};
            AuthContext=InterfaceStub.Create<IAuthorizationContext>((m,a)=>m.Name=="GetAuthorizationInfo" ? Task.FromResult(Auth) : null);
            Channels=InterfaceStub.Create<IChannelManager>((m,a)=>m.Name switch
            {
                "GetChannelsInternalAsync"=>Task.FromResult(new QueryResult<MediaBrowser.Controller.Channels.Channel>(ChannelVisible ? [new() {Id=PluginId}] : [])),
                "GetChannelItems"=>Task.FromResult(new QueryResult<BaseItemDto>([new() {Id=Guid.NewGuid(),Name="Help"}])),_=>null
            });
            Tv=InterfaceStub.Create<ILiveTvManager>((m,a)=>m.Name switch
            {
                "GetInternalLiveTvFolder"=>View,
                "GetInternalChannels"=>new QueryResult<BaseItem>([Access.Adult,Access.News]),_=>null
            });
            Services=new ServiceCollection().AddLogging().AddSingleton(Access.Store).AddSingleton(Access.Groups).AddSingleton(Access.UserManager).AddSingleton(Library).AddSingleton(Manager).AddSingleton(Channels).AddSingleton(Tv).AddSingleton(AuthContext).AddSingleton<NativeGuideService>().AddSingleton<NativeGuideActionService>().BuildServiceProvider();
            Scopes=Services.GetRequiredService<NativeGuideService>();Actions=Services.GetRequiredService<NativeGuideActionService>();SetPrincipal(client);Http.RequestServices=Services;Http.Request.Headers.AcceptLanguage="en";
        }
        public void SetPrincipal(string client)=>Http.User=new(new ClaimsIdentity([new("Jellyfin-UserId",User.Id.ToString()),new("Jellyfin-DeviceId","tv"),new("Jellyfin-Client",client),new("Jellyfin-IsApiKey","False")],"fixture"));
        public void Dispose() {Services.Dispose();Access.Dispose();}
    }
}
}
namespace Jellyfin.Api.Controllers {internal class ItemsController {} internal class ChannelsController {}}
