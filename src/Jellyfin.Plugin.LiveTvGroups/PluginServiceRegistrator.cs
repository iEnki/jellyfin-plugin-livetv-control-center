using System.IO;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using Jellyfin.Plugin.LiveTvGroups.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LiveTvGroups;

/// <summary>
/// Registers the plugin services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(sp =>
        {
            var directory = Plugin.Instance?.DataFolderPath
                ?? Path.Combine(sp.GetRequiredService<IApplicationPaths>().PluginsPath, "LiveTvGroups");
            return new GroupStore(Path.Combine(directory, "users"));
        });
        serviceCollection.AddSingleton<ChannelAccessService>();
        serviceCollection.AddSingleton<ChannelAccessTagBridge>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<ChannelAccessTagBridge>());
        serviceCollection.AddSingleton<ChannelAccessRevoker>();
        serviceCollection.AddScoped<ChannelAccessFilter>();
        serviceCollection.Configure<MvcOptions>(options => options.Filters.AddService<ChannelAccessFilter>());
        serviceCollection.AddSingleton<GroupService>();
        serviceCollection.AddSingleton<GroupArtworkService>();
        serviceCollection.AddSingleton<NativeGuideService>();
        serviceCollection.AddSingleton<NativeGuideActionService>();
        serviceCollection.AddScoped<NativeGuideActionFilter>();
        serviceCollection.PostConfigure<MvcOptions>(options => options.Filters.AddService<NativeGuideActionFilter>(-800));
        serviceCollection.AddScoped<WholphinCompatibilityFilter>();
        serviceCollection.PostConfigure<MvcOptions>(options => options.Filters.AddService<WholphinCompatibilityFilter>(-850));
        serviceCollection.AddScoped<NativeGuideChannelFilter>();
        serviceCollection.PostConfigure<MvcOptions>(options => options.Filters.AddService<NativeGuideChannelFilter>(-900));
        serviceCollection.AddSingleton<PlaylistSyncService>();
        serviceCollection.AddSingleton<WebInjectionStatus>();
        serviceCollection.AddSingleton<IChannel, GroupsChannel>();
        serviceCollection.AddHostedService<WebInjectionService>();
        serviceCollection.AddHostedService<PluginPagesService>();
        serviceCollection.AddHostedService<PlaylistSyncTrigger>();
        serviceCollection.AddHostedService<ChannelDisplayNameService>();
        serviceCollection.AddSingleton<PlayerService>();
        serviceCollection.AddSingleton<GroupGuideService>();
        serviceCollection.AddSingleton<LiveTvStreamBridge>();
        serviceCollection.AddSingleton<AppGuideService>();
        // GroupsMediaSourceProvider is discovered by Jellyfin through IServerApplicationHost.GetExports.
    }
}
