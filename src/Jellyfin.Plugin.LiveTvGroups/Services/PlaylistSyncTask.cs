using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// Periodically mirrors the groups as playlists and cleans them up when the feature is disabled.
/// </summary>
public class PlaylistSyncTask : IScheduledTask
{
    private readonly PlaylistSyncService _syncService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistSyncTask"/> class.
    /// </summary>
    /// <param name="syncService">Playlist sync service.</param>
    public PlaylistSyncTask(PlaylistSyncService syncService)
    {
        _syncService = syncService;
    }

    /// <inheritdoc />
    public string Name => "Live-TV Gruppen als Wiedergabelisten synchronisieren";

    /// <inheritdoc />
    public string Key => "LiveTvGroupsPlaylistSync";

    /// <inheritdoc />
    public string Description => "Legt für jede Sendergruppe eine Wiedergabeliste an (für Apps ohne Kanal-Unterstützung wie Wholphin) und hält sie aktuell.";

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
