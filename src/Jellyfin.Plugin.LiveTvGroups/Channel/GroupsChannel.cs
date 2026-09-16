using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Channel;

/// <summary>
/// Exposes the groups of each user as a channel so native apps can browse them.
/// </summary>
public class GroupsChannel : IChannel, IHasCacheKey, IRequiresMediaInfoCallback
{
    /// <summary>
    /// The channel name; Jellyfin derives the internal channel id from it.
    /// </summary>
    public const string ChannelName = "Live-TV Gruppen";

    private const string GroupPrefix = "ltvgroup_";
    private const string ChannelPrefix = "ltvchannel_";

    private readonly GroupService _groups;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GroupsChannel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupsChannel"/> class.
    /// </summary>
    /// <param name="groups">Group service.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="logger">Logger.</param>
    /// <remarks>
    /// Jellyfin services are resolved lazily: this channel is created while the channel manager is being
    /// constructed, and most live TV services depend on the channel manager (circular dependency at startup).
    /// </remarks>
    public GroupsChannel(GroupService groups, IServiceProvider serviceProvider, ILogger<GroupsChannel> logger)
    {
        _groups = groups;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private IUserManager UserManager => _serviceProvider.GetRequiredService<IUserManager>();

    /// <inheritdoc />
    public string Name => ChannelName;

    /// <inheritdoc />
    public string Description => "Eigene Sendergruppen aus Live-TV.";

    /// <inheritdoc />
    public string DataVersion => "1";

    /// <inheritdoc />
    public string HomePageUrl => "https://github.com/iEnki/jellyfin-plugin-livetv-groups";

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures()
    {
        return new InternalChannelFeatures
        {
            MediaTypes = [ChannelMediaType.Video],
            ContentTypes = [ChannelMediaContentType.Clip]
        };
    }

    /// <inheritdoc />
    public bool IsEnabledFor(string userId)
    {
        if (Plugin.Instance?.Configuration.EnableAppChannel != true || !Guid.TryParse(userId, out var id))
        {
            return false;
        }

        var user = UserManager.GetUserById(id);
        return user is not null && user.HasPermission(PermissionKind.EnableLiveTvAccess);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Jellyfin caches channel results for hours and only separates users via this key.
    /// The revision makes changes to the groups visible immediately.
    /// </remarks>
    public string? GetCacheKey(string? userId)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            return null;
        }

        var revision = _groups.Store.Get(id).Revision;
        return id.ToString("N", CultureInfo.InvariantCulture) + "-" + revision.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var user = query.UserId.Equals(Guid.Empty) ? null : UserManager.GetUserById(query.UserId);
        if (user is null)
        {
            return Task.FromResult(new ChannelItemResult());
        }

        var doc = _groups.Store.Get(user.Id);
        List<ChannelItemInfo> items;

        if (string.IsNullOrEmpty(query.FolderId))
        {
            items = doc.Groups.Select(g => new ChannelItemInfo
            {
                Id = GetFolderExternalId(g.Id),
                Name = g.Name,
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container
            }).ToList();
        }
        else
        {
            var group = query.FolderId.StartsWith(GroupPrefix, StringComparison.Ordinal)
                && Guid.TryParse(query.FolderId[GroupPrefix.Length..], out var groupId)
                    ? doc.Groups.FirstOrDefault(g => g.Id == groupId)
                    : null;

            if (group is null)
            {
                return Task.FromResult(new ChannelItemResult());
            }

            items = _groups.ResolveChannels(user, group, _groups.GetAccessibleChannels(user))
                .Select((channel, index) => new ChannelItemInfo
                {
                    Id = GetItemExternalId(group.Id, channel.Id),
                    Name = channel.Name,
                    Type = ChannelItemType.Media,
                    MediaType = ChannelMediaType.Video,
                    ContentType = ChannelMediaContentType.Clip,
                    IsLiveStream = true,
                    IndexNumber = index + 1,
                    ImageUrl = GetRemoteLogoUrl(channel)
                })
                .ToList();
        }

        return Task.FromResult(new ChannelItemResult { Items = items, TotalRecordCount = items.Count });
    }

    /// <inheritdoc />
    /// <remarks>
    /// Channel items cannot use the regular live TV media sources: the channel media source provider
    /// does not support opening streams. The tuner source is therefore returned as a plain remote stream
    /// that the server remuxes, so the tuner URL (with provider credentials) is not handed to clients for direct play.
    /// </remarks>
    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        if (!id.StartsWith(ChannelPrefix, StringComparison.Ordinal)
            || !Guid.TryParse(id[^32..], out var itemId)
            || _serviceProvider.GetRequiredService<ILibraryManager>().GetItemById(itemId) is not LiveTvChannel channel
            || string.IsNullOrEmpty(channel.ExternalId))
        {
            return [];
        }

        foreach (var host in _serviceProvider.GetRequiredService<ITunerHostManager>().TunerHosts)
        {
            try
            {
                var sources = await host.GetChannelStreamMediaSources(channel.ExternalId, cancellationToken).ConfigureAwait(false);
                if (sources.Count == 0)
                {
                    continue;
                }

                return sources.Select((source, index) =>
                {
                    // Jellyfin does not assign ids to channel media sources; clients need one to request the stream.
                    source.Id = (id + "_" + index.ToString(CultureInfo.InvariantCulture)).GetMD5().ToString("N", CultureInfo.InvariantCulture);
                    source.Container ??= GetContainer(source.Path);
                    source.SupportsProbing = true;
                    source.RequiresOpening = false;
                    source.RequiresClosing = false;
                    source.OpenToken = null;
                    source.LiveStreamId = null;
                    source.SupportsDirectPlay = false;
                    source.IsInfiniteStream = true;
                    return source;
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tuner {Tuner} could not provide a stream for channel {Channel}", host.Name, channel.Name);
            }
        }

        _logger.LogWarning("No stream found for channel {Channel} ({ExternalId})", channel.Name, channel.ExternalId);
        return [];
    }

    /// <summary>
    /// Gets the logo URL of a live TV channel as provided by the tuner (e.g. tvg-logo of an M3U).
    /// Jellyfin downloads channel item images itself, so only absolute http(s) URLs can be used.
    /// </summary>
    private static string? GetRemoteLogoUrl(LiveTvChannel channel)
    {
        var path = channel.GetImageInfo(ImageType.Primary, 0)?.Path;
        return path is not null
            && (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                ? path
                : null;
    }

    /// <summary>
    /// Derives the container from the stream URL (e.g. ".ts"); manifests and unknown extensions are left to probing.
    /// </summary>
    internal static string? GetContainer(string? path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var extension = System.IO.Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
        return extension is "ts" or "mp4" or "mkv" or "flv" ? extension : null;
    }

    /// <summary>
    /// Gets the external id of a group folder.
    /// </summary>
    /// <param name="groupId">Group id.</param>
    /// <returns>The external id.</returns>
    public static string GetFolderExternalId(Guid groupId) => GroupPrefix + groupId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the external id of a channel inside a group. It is unique per group because Jellyfin deletes
    /// channel items that disappear from a folder, which would otherwise affect other groups with the same channel.
    /// </summary>
    /// <param name="groupId">Group id.</param>
    /// <param name="channelId">Live TV channel item id.</param>
    /// <returns>The external id.</returns>
    public static string GetItemExternalId(Guid groupId, Guid channelId)
        => ChannelPrefix + groupId.ToString("N", CultureInfo.InvariantCulture) + "_" + channelId.ToString("N", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages() => [];
}
