using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LiveTvGroups.Model;

/// <summary>Personal preferences; never applied to native Live TV queries.</summary>
public class GroupPreferences
{
    public List<Guid> HiddenGroupIds { get; set; } = [];
    public Guid? DefaultGroupId { get; set; }
    public string DefaultView { get; set; } = "guide";
    public int Zoom { get; set; } = 5;
    public bool RememberLastView { get; set; } = true;
    public Guid? LastGroupId { get; set; }
    public string LastView { get; set; } = "guide";

    public static bool IsValidView(string view) => view is "programs" or "guide" or "channels";
}
