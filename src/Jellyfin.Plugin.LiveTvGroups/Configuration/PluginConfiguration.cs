using System;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LiveTvGroups.Configuration;

/// <summary>
/// Global plugin settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    private string? _displayName;
    /// <summary>Optional server-wide display name. Empty uses the localized default.</summary>
    public string? DisplayName
    {
        get => _displayName;
        set
        {
            var name = value?.Trim();
            if (name is { Length: > 100 } || (name?.Any(char.IsControl) ?? false))
                throw new ArgumentException("Invalid display name. Use at most 100 characters without control characters.");
            _displayName = string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }

    /// <summary>Gets or sets the timezone used in native app program lists.</summary>
    public string AppGuideTimeZone { get; set; } = "Europe/Vienna";
    /// <summary>
    /// Gets or sets a value indicating whether the groups view is injected into the web client.
    /// </summary>
    public bool EnableWebIntegration { get; set; } = true;

    /// <summary>Hide only the original My Media entry in web clients when a usable groups entry is present.</summary>
    public bool HideOriginalLiveTvHomeEntry { get; set; }

    internal bool ShouldHideOriginalLiveTvHomeEntry(bool hasAccessibleChannel)
        => HideOriginalLiveTvHomeEntry && EnableWebIntegration && EnableAppChannel && hasAccessibleChannel;

    /// <summary>
    /// Gets or sets a value indicating whether groups are exposed as a channel for native apps.
    /// </summary>
    public bool EnableAppChannel { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether groups are mirrored as playlists for apps without channel support.
    /// </summary>
    public bool EnablePlaylistSync { get; set; }

    /// <summary>Show official Android TV / Fire TV sessions as web guide targets.</summary>
    public bool EnableJellyfinTvTargets { get; set; } = true;

    /// <summary>Show Wholphin sessions as web guide targets.</summary>
    public bool EnableWholphinTargets { get; set; } = true;
}
