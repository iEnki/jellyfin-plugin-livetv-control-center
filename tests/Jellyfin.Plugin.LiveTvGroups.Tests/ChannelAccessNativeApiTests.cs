using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
namespace Jellyfin.Plugin.LiveTvGroups.Tests;
/// <summary>Run with JELLYFIN_NATIVE_API pointing to the API built from the Controller package's exact source commit.</summary>
public class ChannelAccessNativeApiTests
{
    [NativeApiFact]
    public async Task ActualJellyfinControllersUseRegisteredFilterAndNativePagination()
    {
        var path = Environment.GetEnvironmentVariable("JELLYFIN_NATIVE_API")!;
        Assert.True(File.Exists(path));
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            var file = Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");
            return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var api = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            using var f = new AccessFixture(); f.Enable(); f.Store.UpdateAdministration(d => { d.ChannelAccess.Recordings.Add(new() { ItemId = f.Recording.Id, Path = f.Recording.Path, Channel = Jellyfin.Plugin.LiveTvGroups.Services.ChannelAccessService.Reference(f.Adult) }); return true; });
            var hostStub = InterfaceStub.Create<IServerApplicationHost>((m, a) => null);
            using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(s =>
            {
                new PluginServiceRegistrator().RegisterServices(s, hostStub);
                s.AddSingleton(f.Store).AddSingleton(f.Access).AddSingleton(f.Bridge).AddSingleton(f.Revoker).AddSingleton(f.Groups);
                foreach (var type in new[] { typeof(MediaBrowser.Controller.Library.IUserManager), typeof(MediaBrowser.Controller.Library.ILibraryManager), typeof(MediaBrowser.Controller.LiveTv.ILiveTvManager), typeof(MediaBrowser.Controller.LiveTv.IRecordingsManager), typeof(MediaBrowser.Controller.Library.IMediaSourceManager), typeof(MediaBrowser.Controller.Session.ISessionManager), typeof(MediaBrowser.Controller.MediaEncoding.ITranscodeManager) })
                    s.AddSingleton(type, f.Services.GetRequiredService(type));
                s.AddSingleton(InterfaceStub.Create<MediaBrowser.Controller.Dto.IDtoService>((method, args) => method.Name switch
                {
                    "GetBaseItemDtos" => ((System.Collections.Generic.IEnumerable<MediaBrowser.Controller.Entities.BaseItem>)args![0]!).Select(i => new MediaBrowser.Model.Dto.BaseItemDto() { Id = i.Id, Name = i.Name }).ToArray(),
                    "GetBaseItemDto" => new MediaBrowser.Model.Dto.BaseItemDto() { Id = ((MediaBrowser.Controller.Entities.BaseItem)args![0]!).Id },
                    _ => null
                }));
                s.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null).AddApplicationPart(api).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyNativeControllers()));
                s.AddSingleton<IControllerActivator, NativeActivator>();
                s.AddAuthorization(o => { o.AddPolicy("Download", p => p.RequireAssertion(_ => true)); o.AddPolicy("LiveTvAccess", p => p.RequireAssertion(_ => true)); o.AddPolicy("RequiresElevation", p => p.RequireAssertion(_ => true)); o.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build(); });
            }).Configure(app =>
            {
                app.Use(async (ctx, next) => { if (Guid.TryParse(ctx.Request.Headers["X-Test-User"], out var id)) ctx.User = new(new ClaimsIdentity([new Claim("Jellyfin-UserId", id.ToString()), new Claim("Jellyfin-IsApiKey", "False")], "fixture")); await next(); });
                app.UseRouting(); app.UseAuthorization(); app.UseEndpoints(e => e.MapControllers());
            })).StartAsync();
            var client = host.GetTestClient(); client.DefaultRequestHeaders.Add("X-Test-User", f.Bob.Id.ToString());
            foreach (var route in new[] { "/LiveTv/Channels/" + f.Adult.Id, "/Items/" + f.Adult.Id + "/PlaybackInfo", "/Videos/" + f.Adult.Id + "/stream?Static=true", "/Videos/" + f.News.Id + "/hls/unowned-file/stream.ts", "/Items/" + f.Recording.Id + "/Download", "/Items/" + f.Recording.Id + "/File", "/Videos/" + f.Recording.Id + "/source/Subtitles/0/Stream.vtt", "/Videos/" + f.News.Id + "/source/Subtitles/0/Stream.vtt?itemId=" + f.Recording.Id, "/Videos/" + f.Recording.Id + "/source/Attachments/0" })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
            var visible = await client.GetAsync("/LiveTv/Channels?limit=1&startIndex=0"); Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            var json = System.Text.Json.JsonDocument.Parse(await visible.Content.ReadAsStringAsync()); Assert.Equal(1, json.RootElement.GetProperty("TotalRecordCount").GetInt32()); Assert.Equal(f.News.Id, json.RootElement.GetProperty("Items")[0].GetProperty("Id").GetGuid());
            client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Add("X-Test-User", f.Alice.Id.ToString());
            var alice = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/LiveTv/Channels?limit=1&startIndex=1")); Assert.Equal(2, alice.RootElement.GetProperty("TotalRecordCount").GetInt32()); Assert.Equal(1, alice.RootElement.GetProperty("Items").GetArrayLength());
            client.DefaultRequestHeaders.Remove("X-Test-User"); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Videos/" + f.Recording.Id + "/source/Subtitles/0/Stream.vtt")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/LiveTv/LiveStreamFiles/unknown/stream.ts")).StatusCode);
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }
    private class OnlyNativeControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(System.Collections.Generic.IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { foreach (var type in feature.Controllers.Where(t => t.Namespace != "Jellyfin.Api.Controllers" || t.Name is not ("LiveTvController" or "MediaInfoController" or "VideosController" or "HlsSegmentController" or "LibraryController" or "SubtitleController" or "VideoAttachmentsController")).ToArray()) feature.Controllers.Remove(type); }
    }
    private class NativeActivator : IControllerActivator
    {
        public object Create(ControllerContext context) => Make(context.ActionDescriptor.ControllerTypeInfo.AsType(), context.HttpContext.RequestServices);
        public void Release(ControllerContext context, object controller) { }
        private static object Make(Type type, IServiceProvider services)
        {
            if (services.GetService(type) is { } registered) return registered;
            if (type.IsInterface) return typeof(InterfaceStub).GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [new Func<MethodInfo, object?[]?, object?>((m, a) => null)])!;
            var ctor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
            return ctor.Invoke(ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : Make(p.ParameterType, services)).ToArray());
        }
    }
}

public sealed class NativeApiFactAttribute : FactAttribute
{
    public NativeApiFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JELLYFIN_NATIVE_API"))) Skip = "Run npm run test:native to build and test the supported Jellyfin API."; }
}
