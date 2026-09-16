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
    private const string GuidePrefix = "ltvguide_";
    private const string GuideRootId = GuidePrefix + "root";
    // Changing this prefix re-creates all channel items (e.g. to replace stale images stored by Jellyfin).
    private const string ChannelPrefix = "ltvch4_";

    private static readonly TimeSpan ProbeCacheDuration = TimeSpan.FromHours(6);

    private readonly GroupService _groups;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GroupsChannel> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ProbeResult> _probeCache = new();

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
    public string DataVersion => "4"; // Bump together with ChannelPrefix: Jellyfin caches channel results for 3 hours per data version.

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
    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var user = query.UserId.Equals(Guid.Empty) ? null : UserManager.GetUserById(query.UserId);
        if (user is null)
        {
            return new ChannelItemResult();
        }

        var doc = _groups.Store.Get(user.Id);
        List<ChannelItemInfo> items;

        if (string.IsNullOrEmpty(query.FolderId))
        {
            items = doc.Groups.Select(g => Folder(GetFolderExternalId(g.Id), g.Name)).ToList();
            if (Plugin.Instance?.Configuration.EnableGuideFilter == true && doc.Groups.Count > 0)
            {
                items.Insert(0, Folder(GuideRootId, "📺 Programmführer wählen"));
            }
        }
        else if (query.FolderId.StartsWith(GuidePrefix, StringComparison.Ordinal))
        {
            items = GetGuideItems(user.Id, query.FolderId);
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

            items = [];
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
                    IndexNumber = items.Count + 1,
                    ImageUrl = await GetLocalLogoPathAsync(channel).ConfigureAwait(false)
                });
            }
        }

        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private static ChannelItemInfo Folder(string id, string name)
        => new() { Id = id, Name = name, Type = ChannelItemType.Folder, FolderType = ChannelFolderType.Container };

    /// <summary>
    /// Items of the "choose program guide" folder. Opening a group entry makes it the active guide group, which
    /// <see cref="Web.GuideFilterMiddleware"/> applies to the channel list of TV apps.
    /// Jellyfin only stores names when an item is created, so the active state is part of the item ids.
    /// </summary>
    private List<ChannelItemInfo> GetGuideItems(Guid userId, string folderId)
    {
        var doc = _groups.Store.Get(userId);

        if (folderId == GuideRootId)
        {
            var active = doc.ActiveGuideGroupId;
            var list = new List<ChannelItemInfo>
            {
                Folder(GuidePrefix + "all_" + (active is null ? "1" : "0"), (active is null ? "✔ " : string.Empty) + "Alle Sender")
            };
            list.AddRange(doc.Groups.Select(g =>
            {
                var isActive = g.Id == active;
                return Folder(
                    GuidePrefix + "g_" + g.Id.ToString("N", CultureInfo.InvariantCulture) + "_" + (isActive ? "1" : "0"),
                    (isActive ? "✔ " : string.Empty) + g.Name);
            }));
            return list;
        }

        Guid? selected = null;
        string name = "Alle Sender";
        if (folderId.StartsWith(GuidePrefix + "g_", StringComparison.Ordinal)
            && Guid.TryParse(folderId.Substring(GuidePrefix.Length + 2, 32), out var groupId))
        {
            var group = doc.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group is null)
            {
                return [];
            }

            selected = group.Id;
            name = group.Name;
        }
        else if (!folderId.StartsWith(GuidePrefix + "all_", StringComparison.Ordinal))
        {
            return [];
        }

        if (doc.ActiveGuideGroupId != selected)
        {
            _groups.Store.Update(userId, d =>
            {
                d.ActiveGuideGroupId = selected;
                return true;
            });
        }

        return [Folder(GuidePrefix + "info_" + (selected?.ToString("N", CultureInfo.InvariantCulture) ?? "all"), "✔ Aktiv: " + name + " – jetzt Live-TV → Programmführer öffnen")];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Channel items cannot use the regular live TV media sources: the channel media source provider
    /// does not support opening streams. The tuner source is therefore returned as a plain remote stream
    /// that the server remuxes, so the tuner URL (with provider credentials) is not handed to clients for direct play.
    /// </remarks>
    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        // Items created by older versions ("ltvchannel_", "ltvch2_", "ltvch3_") may still be cached by Jellyfin or referenced by playlists.
        if (!id.StartsWith("ltvch", StringComparison.Ordinal)
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

                var result = sources.Select((source, index) =>
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
                    source.AnalyzeDurationMs = 3000;
                    return source;
                }).ToList();

                await AddStreamInfoAsync(channel, result[0], cancellationToken).ConfigureAwait(false);
                return result;
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
    /// Adds codec information to the source. Without it Jellyfin probes the live stream for minutes and transcodes
    /// with worst-case settings; regular live TV avoids this by probing when the stream is opened.
    /// </summary>
    private async Task AddStreamInfoAsync(LiveTvChannel channel, MediaSourceInfo source, CancellationToken cancellationToken)
    {
        if (!_probeCache.TryGetValue(channel.Id, out var cached) || cached.Expires < DateTime.UtcNow)
        {
            try
            {
                var probeSource = new MediaSourceInfo
                {
                    Path = source.Path,
                    Protocol = source.Protocol,
                    Container = source.Container,
                    IsRemote = source.IsRemote,
                    IsInfiniteStream = true,
                    SupportsProbing = true,
                    RequiredHttpHeaders = source.RequiredHttpHeaders,
                    MediaStreams = []
                };

                await _serviceProvider.GetRequiredService<IMediaSourceManager>()
                    .AddMediaInfoWithProbe(probeSource, false, null, false, true, cancellationToken)
                    .ConfigureAwait(false);

                cached = new ProbeResult(
                    probeSource.MediaStreams
                        .Where(s => s.Type == MediaStreamType.Video).Take(1)
                        .Concat(probeSource.MediaStreams.Where(s => s.Type == MediaStreamType.Audio).Take(1))
                        .ToList(),
                    probeSource.Bitrate,
                    probeSource.Container,
                    DateTime.UtcNow.Add(ProbeCacheDuration));

                if (cached.Streams.Count > 0)
                {
                    _probeCache[channel.Id] = cached;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not probe stream of channel {Channel}", channel.Name);
                return;
            }
        }

        if (cached.Streams.Count == 0)
        {
            return;
        }

        // Like regular live TV: stream indexes of live sources are not stable, so let ffmpeg pick the first ones.
        source.MediaStreams = cached.Streams.Select(s =>
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<MediaStream>(System.Text.Json.JsonSerializer.Serialize(s))!;
            copy.Index = -1;
            copy.Language = null;
            return copy;
        }).ToList();
        source.Bitrate = cached.Bitrate;
        source.Container ??= cached.Container;
    }

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

    private sealed record ProbeResult(IReadOnlyList<MediaStream> Streams, int? Bitrate, string? Container, DateTime Expires);
}
