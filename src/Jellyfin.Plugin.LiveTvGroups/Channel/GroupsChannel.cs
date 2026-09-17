using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Channel;

/// <summary>
/// Exposes the groups of each user as a channel so native apps can browse them.
/// </summary>
public class GroupsChannel : IChannel, IHasCacheKey
{
    /// <summary>
    /// The channel name; Jellyfin derives the internal channel id from it.
    /// </summary>
    public const string ChannelName = "Live-TV Gruppen";

    private const string GroupPrefix = "ltvgroup_";
    // Changing this prefix re-creates all channel items (e.g. to replace stale images stored by Jellyfin).
    private const string ChannelPrefix = "ltvch4_";

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
    public string DataVersion => "6"; // Invalidates cached guide-selection folders while preserving existing channel/playback item IDs.

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
        if ((Plugin.Instance?.Configuration.EnableAppChannel != true && Plugin.Instance?.Configuration.EnableWebIntegration != true) || !Guid.TryParse(userId, out var id))
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
        return id.ToString("N", CultureInfo.InvariantCulture) + "-" + revision.ToString(CultureInfo.InvariantCulture) + "-"
            + (DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(5).Ticks).ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var user = query.UserId.Equals(Guid.Empty) ? null : UserManager.GetUserById(query.UserId);
        if (user is null || !user.HasPermission(PermissionKind.EnableLiveTvAccess))
        {
            return new ChannelItemResult();
        }

        var doc = _groups.Store.Get(user.Id);
        List<ChannelItemInfo> items;

        if (string.IsNullOrEmpty(query.FolderId))
        {
            items = doc.Groups.Select(g => Folder(GetFolderExternalId(g.Id), g.Name)).ToList();
        }
        else if (AppGuideService.Handles(query.FolderId))
        {
            return await _serviceProvider.GetRequiredService<AppGuideService>().GetItems(user, query.FolderId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var group = query.FolderId.StartsWith(GroupPrefix, StringComparison.Ordinal)
                && Guid.TryParse(query.FolderId[GroupPrefix.Length..], out var groupId)
                    ? doc.Groups.FirstOrDefault(g => g.Id == groupId)
                    : null;

            if (group is null)
            {
                return new ChannelItemResult();
            }

            var epg = Folder(AppGuideService.GetRootId(group.Id), "Fernsehprogramm");
            epg.IndexNumber = 0; epg.Overview = "Programmdaten dieser Gruppe nach Tagen und Sendern. Der originale Live-TV-Guide bleibt unverändert.";
            items = [epg];
            foreach (var channel in _groups.ResolveChannels(user, group, _groups.GetAccessibleChannels(user)))
            {
                items.Add(new ChannelItemInfo
                {
                    Id = GetItemExternalId(group.Id, channel.Id),
                    Name = channel.Name,
                    Type = ChannelItemType.Media,
                    MediaType = ChannelMediaType.Video,
                    ContentType = ChannelMediaContentType.Clip,
                    IsLiveStream = true,
                    IndexNumber = items.Count,
                    ImageUrl = await GetLocalLogoPathAsync(channel).ConfigureAwait(false)
                });
            }
        }

        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private static ChannelItemInfo Folder(string id, string name)
        => new() { Id = id, Name = name, Type = ChannelItemType.Folder, FolderType = ChannelFolderType.Container };

    /// <summary>
    /// Gets a local logo file for a live TV channel. Channel items get their image only once, when they are created,
    /// and remote tuner logos often fail to download for them (e.g. provider rate limits). The logo is therefore stored
    /// locally for the original channel first, one channel at a time, and the local file is passed on.
    /// </summary>
    private async Task<string?> GetLocalLogoPathAsync(LiveTvChannel channel)
    {
        var image = channel.GetImageInfo(ImageType.Primary, 0);
        if (image is null || string.IsNullOrEmpty(image.Path))
        {
            return null;
        }

        if (image.IsLocalFile)
        {
            return image.Path;
        }

        try
        {
            var local = await _serviceProvider.GetRequiredService<ILibraryManager>()
                .ConvertImageToLocal(channel, image, 0, false)
                .ConfigureAwait(false);
            return local.IsLocalFile ? local.Path : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not download logo of channel {Channel}", channel.Name);
            return null;
        }
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
