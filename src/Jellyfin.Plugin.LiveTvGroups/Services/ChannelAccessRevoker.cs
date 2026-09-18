using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Jellyfin.Plugin.LiveTvGroups.Services;
/// <summary>Revokes HTTP consumers and device jobs individually, without closing another user's shared tuner.</summary>
public class ChannelAccessRevoker(ChannelAccessService access, IServiceProvider services, ILogger<ChannelAccessRevoker> logger)
{
    public sealed record Lease(Guid UserId, Guid ItemId, string? DeviceId, string? PlaySessionId, string? LiveStreamId);
    private readonly ConcurrentDictionary<HttpContext, Lease> _requests = new();
    private readonly ConcurrentDictionary<string, Lease> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _streams = new(StringComparer.Ordinal);
    public Guid? Stream(string id) => _streams.TryGetValue(id, out var source) ? source : null;
    public void AssociateStream(Lease lease, string? liveId, string? sourceId)
    {
        if (string.IsNullOrEmpty(liveId)) return;
        _streams[liveId] = lease.ItemId;
        if (!string.IsNullOrEmpty(sourceId)) _streams[sourceId] = lease.ItemId;
        var live = services.GetService<IMediaSourceManager>()?.GetLiveStreamInfo(liveId);
        var unique = live?.GetType().GetProperty("UniqueId")?.GetValue(live)?.ToString();
        if (!string.IsNullOrEmpty(unique)) _streams[unique] = lease.ItemId;
    }
    private readonly ConcurrentDictionary<string, Lease> _files = new(StringComparer.Ordinal);
    public void Track(HttpContext context, Lease lease)
    {
        _requests[context] = lease;
        if (!string.IsNullOrEmpty(lease.PlaySessionId)) _jobs[lease.PlaySessionId] = lease;
    }
    public void Complete(HttpContext context) => _requests.TryRemove(context, out _);
    public Lease? Job(string? id) => id is not null && _jobs.TryGetValue(id, out var lease) ? lease : null;
    public Lease? File(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _files.OrderByDescending(k => k.Key.Length).FirstOrDefault(k => id.StartsWith(k.Key, StringComparison.Ordinal)).Value;
    }
    public void AssociateJob(Lease lease)
    {
        if (string.IsNullOrEmpty(lease.PlaySessionId)) return;
        _jobs[lease.PlaySessionId] = lease;
        var job = services.GetService<ITranscodeManager>()?.GetTranscodingJob(lease.PlaySessionId);
        if (job is not null && !string.IsNullOrEmpty(job.Path)) _files[System.IO.Path.GetFileNameWithoutExtension(job.Path)] = lease;
    }
    public bool Allowed(Lease lease)
    {
        var user = services.GetRequiredService<IUserManager>().GetUserById(lease.UserId);
        var item = services.GetRequiredService<ILibraryManager>().GetItemById(lease.ItemId);
        return user is not null && item is not null && access.ItemAllowed(user, item);
    }
    public async Task RevokeAsync(CancellationToken token)
    {
        foreach (var request in _requests.ToArray()) if (!Allowed(request.Value)) request.Key.Abort();
        var sessions = services.GetService<ISessionManager>();
        if (sessions is not null)
            foreach (var session in sessions.Sessions.ToList())
            {
                var item = session.FullNowPlayingItem ?? (session.NowPlayingItem is { } dto ? services.GetRequiredService<ILibraryManager>().GetItemById(dto.Id) : null);
                var user = services.GetRequiredService<IUserManager>().GetUserById(session.UserId);
                if (user is null || item is null || access.ItemAllowed(user, item)) continue;
                try { await sessions.SendPlaystateCommand(null!, session.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, token).ConfigureAwait(false); }
                catch (Exception error) { logger.LogWarning(error, "Stop command failed for revoked playback session {SessionId}; server stream checks still enforce the restriction.", session.Id); }
            }
        var transcoder = services.GetService<ITranscodeManager>();
        foreach (var job in _jobs.ToArray())
        {
            if (Allowed(job.Value)) continue;
            if (transcoder is not null && !string.IsNullOrEmpty(job.Value.DeviceId))
            {
                try { await transcoder.KillTranscodingJobs(job.Value.DeviceId, job.Key, _ => true).ConfigureAwait(false); }
                catch (Exception error) { logger.LogWarning(error, "Failed to stop revoked transcoding job {PlaySessionId}; API checks still deny subsequent requests.", job.Key); continue; }
            }
            // Keep file ownership as a denial tombstone: cached anonymous segments must not become unowned and readable.
            _jobs.TryRemove(job.Key, out _);
        }
    }
}
