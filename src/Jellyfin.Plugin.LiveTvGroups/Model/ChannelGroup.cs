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
    /// Gets or sets personal preferences for the independent groups page.
    /// </summary>
    public GroupPreferences Preferences { get; set; } = new();

    /// <summary>Explicit native guide selection, keyed by Jellyfin device ID. Legacy guide selections are ignored.</summary>
    public Dictionary<string, Guid> NativeGuideScopes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Devices explicitly using the dynamic union of visible groups; mutually exclusive with a single-group scope.</summary>
    public HashSet<string> NativeGuideVisibleGroupDevices { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A named group of live TV channels.
/// </summary>
public class ChannelGroup
{
    /// <summary>Gets or sets whether every Live TV user may see this shared group.</summary>
    public bool VisibleToAllUsers { get; set; } = true;
    /// <summary>Gets or sets users allowed when universal access is disabled.</summary>
    public List<Guid> AllowedUserIds { get; set; } = [];
    /// <summary>Gets or sets users excluded from a shared group. Denials take precedence.</summary>
    public List<Guid> DeniedUserIds { get; set; } = [];

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
    public string? ServiceName { get; set; }
    public string? ExternalId { get; set; }
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
