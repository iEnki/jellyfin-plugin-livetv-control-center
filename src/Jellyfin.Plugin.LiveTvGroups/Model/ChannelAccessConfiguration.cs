using System;
using System.Collections.Generic;
namespace Jellyfin.Plugin.LiveTvGroups.Model;

public class ChannelAccessConfiguration
{
    public bool Enabled { get; set; }
    public int PolicyRevision { get; set; }
    public List<ChannelAccessRule> Rules { get; set; } = [];
    public List<ChannelRef> KnownChannels { get; set; } = [];
    public List<RecordingChannelAssignment> Recordings { get; set; } = [];
}
public class ChannelAccessRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool VisibleToAllUsers { get; set; }
    public List<Guid> AllowedUserIds { get; set; } = [];
    public List<Guid> DeniedUserIds { get; set; } = [];
    public List<ChannelRef> Channels { get; set; } = [];
}
public class RecordingChannelAssignment
{
    public Guid ItemId { get; set; }
    public string? Path { get; set; }
    public ChannelRef Channel { get; set; } = new();
}
public class ChannelAccessRequest
{
    public int Revision { get; set; }
    public bool Enabled { get; set; }
    public List<ChannelAccessRule> Rules { get; set; } = [];
    public List<RecordingChannelAssignment> Recordings { get; set; } = [];
    public Guid? PreviewUserId { get; set; }
}
