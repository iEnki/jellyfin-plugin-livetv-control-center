using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Uses Jellyfin's existing tuner provider without constructing or opening a second media-source manager.</summary>
public class LiveTvStreamBridge(IServiceProvider services)
{
    // Jellyfin discovers these providers via application-host exports, not IEnumerable<> in DI.
    // Instantiate only its native provider and retain it. Enumerating all exports would construct other plugins again.
    private readonly Lazy<IMediaSourceProvider> _native = new(() =>
        services.GetRequiredService<IServerApplicationHost>().GetExports<IMediaSourceProvider>(type =>
            type.FullName == "Jellyfin.LiveTv.LiveTvMediaSourceProvider"
                ? ActivatorUtilities.CreateInstance(services, type) : null!)
            .SingleOrDefault() ?? throw new InvalidOperationException("Jellyfins Live-TV-Medienanbieter ist nicht verfügbar."));
    private IMediaSourceProvider NativeProvider => _native.Value;

    public virtual Task<IEnumerable<MediaSourceInfo>> GetMediaSources(LiveTvChannel channel, CancellationToken cancellationToken)
        => NativeProvider.GetMediaSources(channel, cancellationToken);

    public virtual Task<ILiveStream> OpenMediaSource(string token, List<ILiveStream> currentStreams, CancellationToken cancellationToken)
        => NativeProvider.OpenMediaSource(token, currentStreams, cancellationToken);
}
