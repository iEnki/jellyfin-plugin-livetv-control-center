using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;
namespace Jellyfin.Plugin.LiveTvGroups.Services;
/// <summary>Captures DVR provenance before new recordings are saved, independently of NFO settings.</summary>
public class ChannelAccessRecordingProvider(ChannelAccessService access, GroupStore store, IServiceProvider services)
    : ICustomMetadataProvider<Video>, ICustomMetadataProvider<Movie>, ICustomMetadataProvider<Episode>, IHasOrder
{
    public string Name => "Live-TV Control Center channel access";
    public int Order => int.MaxValue;
    public Task<ItemUpdateType> FetchAsync(Video item, MetadataRefreshOptions options, CancellationToken cancellationToken) => Fetch(item, cancellationToken);
    public Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken) => Fetch(item, cancellationToken);
    public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken) => Fetch(item, cancellationToken);
    private Task<ItemUpdateType> Fetch(BaseItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!access.Configuration.Enabled || item.Id == Guid.Empty) return Task.FromResult(ItemUpdateType.None);
        // Group-channel videos are not DVR recordings. Capture only active DVR files or previously assigned recordings.
        var assigned = access.Configuration.Recordings.Any(r => r.ItemId == item.Id || !string.IsNullOrEmpty(r.Path) && r.Path == item.Path);
        var active = !string.IsNullOrEmpty(item.Path) && services.GetService<IRecordingsManager>()?.GetActiveRecordingInfo(item.Path) is not null;
        if (!assigned && !active) return Task.FromResult(ItemUpdateType.None);
        var inventory = access.AllChannels(); var sources = access.ItemChannels(item, inventory);
        if (sources.Count == 0) return Task.FromResult(ItemUpdateType.None);
        if (sources.Count == 1 && !store.GetAdministration().ChannelAccess.Recordings.Any(r => r.ItemId == item.Id || !string.IsNullOrEmpty(r.Path) && r.Path == item.Path))
            store.UpdateAdministration(doc =>
            {
                if (!doc.ChannelAccess.Recordings.Any(r => r.ItemId == item.Id || !string.IsNullOrEmpty(r.Path) && r.Path == item.Path)) doc.ChannelAccess.Recordings.Add(new RecordingChannelAssignment
                { ItemId = item.Id, Path = item.Path, Channel = ChannelAccessService.Reference(sources[0]) }); return true;
            });
        var tags = item.Tags.Where(t => !t.StartsWith(ChannelAccessService.TagPrefix, StringComparison.Ordinal)).ToList();
        tags.AddRange(sources.Select(c => ChannelAccessService.OriginPrefix + c.Id.ToString("N")));
        tags.AddRange(services.GetRequiredService<IUserManager>().GetUsers().Where(u => !access.ItemAllowed(u, item, inventory)).Select(u => ChannelAccessService.UserTag(u.Id)));
        var updated = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (item.Tags.SequenceEqual(updated)) return Task.FromResult(ItemUpdateType.None);
        item.Tags = updated;
        return Task.FromResult(ItemUpdateType.MetadataEdit);
    }
}
