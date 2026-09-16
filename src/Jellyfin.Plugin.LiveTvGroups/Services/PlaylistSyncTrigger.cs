using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// Re-synchronizes playlists when the plugin settings are saved, so enabling or disabling takes effect immediately.
/// </summary>
public class PlaylistSyncTrigger : IHostedService
{
    private readonly PlaylistSyncService _syncService;
    private readonly ILogger<PlaylistSyncTrigger> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistSyncTrigger"/> class.
    /// </summary>
    /// <param name="syncService">Playlist sync service.</param>
    /// <param name="logger">Logger.</param>
    public PlaylistSyncTrigger(PlaylistSyncService syncService, ILogger<PlaylistSyncTrigger> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    private void OnConfigurationChanged(object? sender, MediaBrowser.Model.Plugins.BasePluginConfiguration e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _syncService.SyncAllAsync(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playlist sync after configuration change failed");
            }
        });
    }
}
