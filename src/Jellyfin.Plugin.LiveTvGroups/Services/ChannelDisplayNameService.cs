using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Updates the visible library name while retaining the original channel ID and access rules.</summary>
public sealed class ChannelDisplayNameService(IServiceProvider services, IHostApplicationLifetime lifetime,
    ILogger<ChannelDisplayNameService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!lifetime.ApplicationStarted.IsCancellationRequested)
            await Task.Delay(100, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateDisplayName(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Could not update the Live-TV Control Center display name; retrying."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task UpdateDisplayName(CancellationToken cancellationToken)
    {
        var library = services.GetRequiredService<ILibraryManager>();
        var id = GroupsChannel.GetInternalId(library);
        var channel = library.GetItemById(id);
        if (channel is null)
        {
            await services.GetRequiredService<IChannelManager>().GetChannelsInternalAsync(new MediaBrowser.Model.Channels.ChannelQuery()).ConfigureAwait(false);
            channel = library.GetItemById(id);
        }
        if (channel is MediaBrowser.Controller.Channels.Channel)
        {
            var name = PluginLocalization.DisplayName(Plugin.Instance?.Configuration, PluginLocalization.ServerLanguage(services));
            var nameChanged = !string.Equals(channel.Name, name, StringComparison.Ordinal);
            if (nameChanged)
            {
                channel.Name = name;
                channel.SortName = null;
            }

            // An existing channel may never have had its dynamic image saved. The web home
            // tile only renders an image when the channel DTO contains a Primary image tag.
            var logo = services.GetRequiredService<GroupArtworkService>().RootLogoPath;
            var imageChanged = !string.Equals(channel.GetImageInfo(ImageType.Primary, 0)?.Path, logo, StringComparison.Ordinal);
            if (imageChanged)
            {
                channel.SetImagePath(ImageType.Primary, 0, services.GetRequiredService<IFileSystem>().GetFileInfo(logo));
                channel.DateModified = DateTime.UtcNow;
                channel.OnMetadataChanged();
            }

            if (nameChanged || imageChanged)
            {
                await library.UpdateItemAsync(channel, null!, imageChanged ? ItemUpdateType.ImageUpdate : ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            }
        }
    }

}
