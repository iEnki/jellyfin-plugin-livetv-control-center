using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Configuration;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Web;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData("de-DE", "Live-TV Gruppen")]
    [InlineData("de-AT", "Live-TV Gruppen")]
    [InlineData("en-US", "Live-TV Groups")]
    [InlineData("en-GB", "Live-TV Groups")]
    [InlineData("fr-FR", "Live-TV Groups")]
    [InlineData(null, "Live-TV Groups")]
    public void AutomaticNamesFollowLanguageWithEnglishFallback(string? language, string expected)
        => Assert.Equal(expected, PluginLocalization.DisplayName(new PluginConfiguration(), language));

    [Fact]
    public void CustomNameIsSharedAndClearingItRestoresAutomaticNaming()
    {
        var configuration = new PluginConfiguration { DisplayName = "  Family TV  " };
        Assert.Equal("Family TV", PluginLocalization.DisplayName(configuration, "de-DE"));
        Assert.Equal("Family TV", PluginLocalization.DisplayName(configuration, "en-US"));
        configuration.DisplayName = "   ";
        Assert.Null(configuration.DisplayName);
        Assert.Equal("Live-TV Gruppen", PluginLocalization.DisplayName(configuration, "de-DE"));
        Assert.Throws<ArgumentException>(() => configuration.DisplayName = new string('x', 101));
        Assert.Throws<ArgumentException>(() => configuration.DisplayName = "Family\nTV");
    }

    [Fact]
    public void NativeLanguageUsesRequestThenServerAndNeverMetadataLanguage()
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var settings = new ServerConfiguration { UICulture = "de-DE", PreferredMetadataLanguage = "fr" };
        using var services = new ServiceCollection().AddSingleton<IHttpContextAccessor>(http)
            .AddSingleton(InterfaceStub.Create<IServerConfigurationManager>((m, a) => m.Name == "get_Configuration" ? settings : null)).BuildServiceProvider();
        Assert.Equal("de-DE", PluginLocalization.Language(services));
        Assert.Equal("Heute", PluginLocalization.Text("Today", PluginLocalization.Language(services)));
        http.HttpContext.Request.Headers.AcceptLanguage = "en-GB,en;q=0.9";
        Assert.Equal("en-GB", PluginLocalization.Language(services));
        Assert.Equal("Today", PluginLocalization.Text("Today", PluginLocalization.Language(services)));
    }

    [Fact]
    public async Task LibraryRenameRetainsIdentityAndDoesNotWriteUnchangedMetadata()
    {
        var id = Guid.NewGuid(); var writes = 0;
        var channel = new MediaBrowser.Controller.Channels.Channel { Id = id, ChannelId = id, Name = GroupsChannel.ChannelName };
        var library = InterfaceStub.Create<ILibraryManager>((method, args) =>
        {
            if (method.Name == "GetNewItemId") { Assert.Equal("Channel Live-TV Gruppen", args![0]); Assert.Equal(typeof(MediaBrowser.Controller.Channels.Channel), args[1]); return id; }
            if (method.Name == "GetItemById") return channel;
            if (method.Name == "UpdateItemAsync") { writes++; Assert.Equal(id, channel.Id);Assert.Equal(id, channel.ChannelId);return Task.CompletedTask; }
            throw new NotSupportedException(method.Name);
        });
        using var services = new ServiceCollection().AddSingleton(library).BuildServiceProvider();
        var service = new ChannelDisplayNameService(services, InterfaceStub.Create<IHostApplicationLifetime>((m,a)=>null), NullLogger<ChannelDisplayNameService>.Instance);
        await service.UpdateDisplayName(CancellationToken.None);
        Assert.Equal("Live-TV Groups", channel.Name);Assert.Equal(1,writes);
        await service.UpdateDisplayName(CancellationToken.None);Assert.Equal(1,writes);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EntryFindsRenamedChannelByIdentityAndRespectsUserChannelQuery(bool hasChannelAccess)
    {
        var user = new Jellyfin.Database.Implementations.Entities.User("viewer", "test", "test") { Id = Guid.NewGuid() };
        var id = Guid.NewGuid();
        var users = InterfaceStub.Create<MediaBrowser.Controller.Library.IUserManager>((m,a)=>m.Name=="GetUserById" ? user : null);
        var library = InterfaceStub.Create<ILibraryManager>((m,a)=>m.Name=="GetNewItemId" ? id : null);
        var channels = InterfaceStub.Create<MediaBrowser.Controller.Channels.IChannelManager>((m,a)=>
        {
            Assert.Equal("GetChannelsAsync",m.Name);
            Assert.Equal(user.Id,Assert.IsType<MediaBrowser.Model.Channels.ChannelQuery>(a![0]).UserId);
            return Task.FromResult(new MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Model.Dto.BaseItemDto>(0,hasChannelAccess ? 1 : 0,
                hasChannelAccess ? [new MediaBrowser.Model.Dto.BaseItemDto { Id=id,Name="Custom renamed channel" }] : []));
        });
        using var services = new ServiceCollection().AddSingleton(library).AddSingleton(channels).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices=services, User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId",user.Id.ToString())],"test")) };
        http.Request.Headers.AcceptLanguage="de-DE";
        var api = new GroupsController(null!,users,null!,new WebInjectionStatus(),null!) { ControllerContext=new ControllerContext { HttpContext=http } };
        var result=Assert.IsType<OkObjectResult>(await api.GetEntry());
        var json=System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        if (hasChannelAccess) Assert.Equal(id,json.GetProperty("ChannelId").GetGuid());
        else Assert.Equal(System.Text.Json.JsonValueKind.Null,json.GetProperty("ChannelId").ValueKind);
        Assert.False(json.GetProperty("HideOriginalLiveTvHomeEntry").GetBoolean());
        Assert.Equal("Live-TV Gruppen",json.GetProperty("DisplayName").GetString());
    }

    [Fact]
    public void ClientScriptIncludesEmbeddedTranslationsAndRuntime()
    {
        var api=new GroupsController(null!,null!,null!,new WebInjectionStatus(),null!);
        var script=Assert.IsType<ContentResult>(api.GetClientScript());
        Assert.Contains("window.LiveTvGroupsTranslations=",script.Content,StringComparison.Ordinal);
        Assert.Contains("window.LiveTvGroupsI18n =",script.Content,StringComparison.Ordinal);
        Assert.Contains("document.documentElement.lang",script.Content,StringComparison.Ordinal);
        Assert.Equal("application/javascript",script.ContentType);
    }
}
