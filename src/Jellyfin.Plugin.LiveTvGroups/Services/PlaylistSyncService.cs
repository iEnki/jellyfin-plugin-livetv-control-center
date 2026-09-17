using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// Mirrors the groups of a user as playlists, for apps that do not support channels (e.g. Wholphin).
/// Live TV channels cannot be added to playlists, so the playlists contain the items of the groups channel.
/// </summary>
public class PlaylistSyncService
{
    private const string PlaylistNamePrefix = "Live-TV: ";

    private readonly GroupService _groups;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PlaylistSyncService> _logger;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistSyncService"/> class.
    /// </summary>
    /// <param name="groups">Group service.</param>
    /// <param name="serviceProvider">Service provider (Jellyfin services are resolved lazily to avoid startup cycles).</param>
    /// <param name="logger">Logger.</param>
    public PlaylistSyncService(GroupService groups, IServiceProvider serviceProvider, ILogger<PlaylistSyncService> logger)
    {
        _groups = groups;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private static bool Enabled => Plugin.Instance?.Configuration is { EnablePlaylistSync: true, EnableAppChannel: true };

    /// <summary>
    /// Schedules a background sync for a user; multiple calls in quick succession are coalesced.
    /// </summary>
    /// <param name="userId">The user id.</param>
    public void QueueSync(Guid userId)
    {
        if (!_pending.TryAdd(userId, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            _pending.TryRemove(userId, out _);
            try
            {
                await SyncUserAsync(userId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playlist sync failed for user {UserId}", userId);
            }
        });
    }

    /// <summary>
    /// Synchronizes the playlists of all users that have groups.
    /// </summary>
    /// <param name="progress">Progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SyncAllAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var userIds = _serviceProvider.GetRequiredService<IUserManager>().GetUsers().Select(u => u.Id)
            .Concat(_groups.Store.GetUserIds()).Distinct().ToList();
        for (var i = 0; i < userIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SyncUserAsync(userIds[i], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Playlist sync failed for user {UserId}", userIds[i]);
            }

            progress.Report(100d * (i + 1) / userIds.Count);
        }
    }

    /// <summary>
    /// Synchronizes the playlists of one user. Removes all synced playlists when the feature is disabled.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SyncUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var user = _serviceProvider.GetRequiredService<IUserManager>().GetUserById(userId);
            if (user is null)
            {
                return;
            }

            var doc = _groups.Store.Get(userId);
            var wanted = Enabled ? _groups.GetGroups(user).Select(g => g.Id).ToHashSet() : [];

            // Remove playlists of deleted groups (or all, when disabled).
            var obsolete = doc.PlaylistIds.Where(p => !wanted.Contains(p.Key)).ToList();
            foreach (var (groupId, playlistId) in obsolete)
            {
                DeletePlaylist(playlistId);
            }

            if (obsolete.Count > 0)
            {
                _groups.Store.Update(userId, d =>
                {
                    foreach (var (groupId, _) in obsolete)
                    {
                        d.PlaylistIds.Remove(groupId);
                    }

                    return true;
                });
            }

            if (wanted.Count == 0)
            {
                return;
            }

            var channelItems = await GetChannelItemsByGroupAsync(user, cancellationToken).ConfigureAwait(false);
            if (channelItems is null)
            {
                _logger.LogWarning("Channel \"{Channel}\" is not available for user {User}; playlists not synced", GroupsChannel.ChannelName, user.Username);
                return;
            }

            var playlistManager = _serviceProvider.GetRequiredService<IPlaylistManager>();
            foreach (var group in _groups.GetGroups(user))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var itemIds = channelItems.TryGetValue(group.Id, out var items) ? items : [];
                await SyncGroupAsync(playlistManager, user, group.Id, group.Name, itemIds).ConfigureAwait(false);
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    private async Task SyncGroupAsync(IPlaylistManager playlistManager, User user, Guid groupId, string groupName, IReadOnlyList<Guid> itemIds)
    {
        var name = PlaylistNamePrefix + groupName;
        var playlist = _groups.Store.Get(user.Id).PlaylistIds.TryGetValue(groupId, out var playlistId)
            ? playlistManager.GetPlaylists(user.Id).FirstOrDefault(p => p.Id.Equals(playlistId))
            : null;

        if (playlist is null)
        {
            var result = await playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = name,
                UserId = user.Id,
                ItemIdList = itemIds,
                MediaType = MediaType.Video
            }).ConfigureAwait(false);

            var createdId = Guid.Parse(result.Id);
            _groups.Store.Update(user.Id, d =>
            {
                d.PlaylistIds[groupId] = createdId;
                return true;
            });
            _logger.LogInformation("Created playlist \"{Name}\" for user {User}", name, user.Username);
            return;
        }

        var current = playlist.GetManageableItems().Select(i => i.Item1.ItemId ?? Guid.Empty).ToList();
        var itemsChanged = !current.SequenceEqual(itemIds);
        var nameChanged = !string.Equals(playlist.Name, name, StringComparison.Ordinal);

        if (itemsChanged || nameChanged)
        {
            await playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
            {
                Id = playlist.Id,
                UserId = user.Id,
                Name = nameChanged ? name : null,
                Ids = itemsChanged ? itemIds : null
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Loads the groups channel for the user, which creates the channel items in the library, and returns
    /// the item ids per group in display order. Returns null if the channel is not available.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<Guid>>?> GetChannelItemsByGroupAsync(User user, CancellationToken cancellationToken)
    {
        var channelManager = _serviceProvider.GetRequiredService<IChannelManager>();
        var channels = await channelManager.GetChannelsInternalAsync(new ChannelQuery { UserId = user.Id }).ConfigureAwait(false);
        var channel = channels.Items.FirstOrDefault(c => string.Equals(c.Name, GroupsChannel.ChannelName, StringComparison.Ordinal));
        if (channel is null)
        {
            return null;
        }

        var folders = await channelManager.GetChannelItemsInternal(
            new InternalItemsQuery(user) { ChannelIds = [channel.Id] },
            new Progress<double>(),
            cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<Guid, IReadOnlyList<Guid>>();
        var accessible = _groups.GetAccessibleChannels(user);

        foreach (var group in _groups.GetGroups(user))
        {
            var folderExternalId = GroupsChannel.GetFolderExternalId(group.Id);
            var folder = folders.Items.FirstOrDefault(f => string.Equals(f.ExternalId, folderExternalId, StringComparison.Ordinal));
            if (folder is null)
            {
                continue;
            }

            var children = await channelManager.GetChannelItemsInternal(
                new InternalItemsQuery(user) { ChannelIds = [channel.Id], ParentId = folder.Id },
                new Progress<double>(),
                cancellationToken).ConfigureAwait(false);

            var byExternalId = children.Items
                .Where(i => i.ExternalId is not null)
                .GroupBy(i => i.ExternalId)
                .ToDictionary(g => g.Key, g => g.First().Id);

            // Keep the group order, which the library query does not preserve.
            result[group.Id] = _groups.ResolveChannels(user, group, accessible)
                .Select(c => byExternalId.TryGetValue(GroupsChannel.GetItemExternalId(group.Id, c.Id), out var id) ? id : Guid.Empty)
                .Where(id => !id.Equals(Guid.Empty))
                .ToList();
        }

        return result;
    }

    private void DeletePlaylist(Guid playlistId)
    {
        var libraryManager = _serviceProvider.GetRequiredService<ILibraryManager>();
        if (libraryManager.GetItemById(playlistId) is Playlist playlist)
        {
            libraryManager.DeleteItem(playlist, new DeleteOptions { DeleteFileLocation = true }, true);
            _logger.LogInformation("Deleted playlist \"{Name}\"", playlist.Name);
        }
    }
}
