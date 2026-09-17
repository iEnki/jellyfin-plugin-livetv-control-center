using System.Collections.Generic;
namespace Jellyfin.Plugin.LiveTvGroups.Model;
/// <summary>Persistent central configuration, separate from personal data.</summary>
public class GroupAdministration
{
    /// <summary>Gets or sets the cache revision.</summary>
    public int Revision { get; set; }
    /// <summary>Gets or sets personal or shared mode.</summary>
    public string Mode { get; set; } = "personal";
    /// <summary>Gets or sets the central groups.</summary>
    public List<ChannelGroup> Groups { get; set; } = [];
}
