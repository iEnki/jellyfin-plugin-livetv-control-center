using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LiveTvGroups.Model;

/// <summary>
/// All groups of a single user.
/// </summary>
public class UserGroups
{
    /// <summary>
    /// Gets or sets the revision, incremented on every change.
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// Gets or sets the groups in display order.
    /// </summary>
    public List<ChannelGroup> Groups { get; set; } = [];

    /// <summary>
    /// Gets or sets the playlists created for the groups, keyed by group id.
    /// </summary>
    public Dictionary<Guid, Guid> PlaylistIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the group shown in the program guide of TV apps; <c>null</c> shows all channels.
    /// </summary>
    public Guid? ActiveGuideGroupId { get; set; }
}

/// <summary>
/// A named group of live TV channels.
/// </summary>
public class ChannelGroup
{
    /// <summary>
    /// Gets or sets the group id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channels in display order.
    /// </summary>
    public List<ChannelRef> Channels { get; set; } = [];
}

/// <summary>
/// Reference to a live TV channel. Name and number allow re-matching after a tuner rescan changed the item id.
/// </summary>
public class ChannelRef
{
    /// <summary>
    /// Gets or sets the Jellyfin item id of the channel.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the channel name at the time it was added.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel number at the time it was added.
    /// </summary>
    public string? Number { get; set; }
}
