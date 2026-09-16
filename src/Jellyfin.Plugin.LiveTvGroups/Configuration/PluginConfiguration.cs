using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LiveTvGroups.Configuration;

/// <summary>
/// Global plugin settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the groups view is injected into the web client.
    /// </summary>
    public bool EnableWebIntegration { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether groups are exposed as a channel for native apps.
    /// </summary>
    public bool EnableAppChannel { get; set; } = true;
}
