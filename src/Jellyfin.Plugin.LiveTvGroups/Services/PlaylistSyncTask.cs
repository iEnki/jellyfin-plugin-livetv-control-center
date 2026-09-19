using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// Periodically mirrors the groups as playlists and cleans them up when the feature is disabled.
/// </summary>
public class PlaylistSyncTask : IScheduledTask
{
    private readonly PlaylistSyncService _syncService;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistSyncTask"/> class.
    /// </summary>
    /// <param name="syncService">Playlist sync service.</param>
    /// <param name="services">Server services.</param>
    public PlaylistSyncTask(PlaylistSyncService syncService, IServiceProvider services)
    {
        _syncService = syncService;
        _services = services;
    }

    /// <inheritdoc />
    public string Name => PluginLocalization.Text("Synchronize Live-TV Control Center playlists", PluginLocalization.ServerLanguage(_services));

    /// <inheritdoc />
    public string Key => "LiveTvGroupsPlaylistSync";

    /// <inheritdoc />
    public string Description => PluginLocalization.Text("Creates and updates playlists for channel groups for apps without channel support, such as Wholphin.", PluginLocalization.ServerLanguage(_services));

    /// <inheritdoc />
    public string Category => "Live TV";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _syncService.SyncAllAsync(progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger },
            new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(6).Ticks }
        ];
    }
}
