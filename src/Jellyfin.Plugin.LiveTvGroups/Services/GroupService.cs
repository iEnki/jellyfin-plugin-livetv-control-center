using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// Combines stored groups with the live TV channels a user is allowed to see.
/// </summary>
public class GroupService
{
    private readonly GroupStore _store;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupService"/> class.
    /// </summary>
    /// <param name="store">Group store.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <remarks>
    /// Jellyfin services are resolved lazily: the channel manager constructs all <c>IChannel</c> instances
    /// and is itself a dependency of <see cref="ILiveTvManager"/>, so constructor injection causes a cycle.
    /// </remarks>
    public GroupService(GroupStore store, IServiceProvider serviceProvider)
    {
        _store = store;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the store.
    /// </summary>
    public GroupStore Store => _store;

    /// <summary>
    /// Gets all live TV channels the user may access, respecting parental control and channel restrictions.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>Channels keyed by item id.</returns>
    public IReadOnlyDictionary<Guid, LiveTvChannel> GetAccessibleChannels(User user)
    {
        var result = _serviceProvider.GetRequiredService<ILiveTvManager>().GetInternalChannels(
            new LiveTvChannelQuery { UserId = user.Id },
            new DtoOptions(false),
            CancellationToken.None);

        return result.Items.OfType<LiveTvChannel>().ToDictionary(c => c.Id);
    }

    /// <summary>
    /// Resolves the channels of a group. Stored ids are repaired when channels were re-created by a tuner rescan.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="group">The group.</param>
    /// <param name="accessible">Result of <see cref="GetAccessibleChannels"/>.</param>
    /// <returns>Channels in display order.</returns>
    public IReadOnlyList<LiveTvChannel> ResolveChannels(
        User user,
        ChannelGroup group,
        IReadOnlyDictionary<Guid, LiveTvChannel> accessible)
    {
        var available = accessible.Values.Select(ToAvailable).ToList();
        var (matched, changed) = ChannelMatcher.Match(group.Channels, available);
        var channels = matched.Select(c => accessible[c.Id]).ToList();

        if (changed)
        {
            // Only repair when every stored reference could be resolved; otherwise keep the old
            // references so channels that are temporarily unavailable are not dropped.
            if (matched.Count == group.Channels.Count)
            {
                _store.Update(user.Id, doc =>
                {
                    var stored = doc.Groups.FirstOrDefault(g => g.Id == group.Id);
                    if (stored is not null)
                    {
                        stored.Channels = channels.Select(ToRef).ToList();
                    }

                    return true;
                });
            }
        }

        return channels;
    }

    /// <summary>
    /// Creates a stored reference for a channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The reference.</returns>
    public static ChannelRef ToRef(LiveTvChannel channel)
        => new() { ItemId = channel.Id, Name = channel.Name ?? string.Empty, Number = channel.Number };

    private static AvailableChannel ToAvailable(LiveTvChannel channel)
        => new(channel.Id, channel.Name ?? string.Empty, channel.Number);
}
