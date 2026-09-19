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
    /// Fixed provider identity. Never localize: Jellyfin derives the channel ID from it.
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

    public static Guid GetInternalId(ILibraryManager library)
        => library.GetNewItemId("Channel " + ChannelName, typeof(MediaBrowser.Controller.Channels.Channel));

    private string T(string key) => PluginLocalization.Text(key, PluginLocalization.Language(_serviceProvider));

    /// <inheritdoc />
    public string Description => T("Grouped Live TV channels.");

    /// <inheritdoc />
    public string DataVersion => "10"; // Refreshes group folders for direct TV-guide selection without changing channel/playback item IDs.

    /// <inheritdoc />
    public string HomePageUrl => "https://github.com/iEnki/jellyfin-plugin-livetv-control-center";

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
        return PluginLocalization.Culture(_serviceProvider).Name + "-" + id.ToString("N", CultureInfo.InvariantCulture) + "-" + revision.ToString(CultureInfo.InvariantCulture) + "-"
            + _groups.Store.GetAdministration().Revision.ToString(CultureInfo.InvariantCulture) + "-"
            + string.Join(",", UserManager.GetUserById(id) is { } user ? _groups.GetGroups(user).Select(g => g.Id) : []) + "-"
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

        var groups = _groups.GetGroups(user);
        List<ChannelItemInfo> items;

        if (string.IsNullOrEmpty(query.FolderId))
        {
            items = [Folder(NativeGuideActionRoute.AllChannelsId, T("All channels (native guide)"))];
            items.AddRange(groups.Select(g => Folder(GetFolderExternalId(g.Id), g.Name)));
        }
        else if (NativeGuideActionRoute.TryParse(query.FolderId, out var nativeGroup))
        {
            // Providers also run from background refreshes and caches. Only the HTTP action filter
            // may activate the authenticated device scope or send navigation commands.
            if (nativeGroup is not null && !groups.Any(g => g.Id == nativeGroup)) return new ChannelItemResult();
            var help = Folder(NativeGuideActionRoute.HelpId(nativeGroup), T("Open Live TV → TV Guide"));
            help.Overview = T("The native TV guide opens in ordinary Live TV. Select TV Guide there. Reopen the app if its old channel list remains cached.");
            items = [help];
        }
        else if (AppGuideService.Handles(query.FolderId))
        {
            return await _serviceProvider.GetRequiredService<AppGuideService>().GetItems(user, query.FolderId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var group = TryParseGroupFolderId(query.FolderId, out var groupId)
                    ? groups.FirstOrDefault(g => g.Id == groupId)
                    : null;

            if (group is null)
            {
                return new ChannelItemResult();
            }

            var epg = Folder(AppGuideService.GetRootId(group.Id), T("Program list (fallback)"));
            epg.IndexNumber = 1; epg.Overview = T("Program data for this group, organized by day and channel. Original Live TV remains unchanged.");
            var native = Folder(NativeGuideActionRoute.GroupId(group.Id), T("Native TV guide"));
            native.IndexNumber = 0;
            native.Overview = T("Open the original Jellyfin TV guide using only this group's channels on this TV.");
            items = [native, epg];
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

    /// <summary>Recognizes a group folder without accepting arbitrary or malformed provider items.</summary>
    public static bool TryParseGroupFolderId(string? externalId, out Guid groupId)
    {
        groupId = Guid.Empty;
        return externalId?.StartsWith(GroupPrefix, StringComparison.Ordinal) == true
            && Guid.TryParseExact(externalId[GroupPrefix.Length..], "N", out groupId)
            && groupId != Guid.Empty;
    }

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
