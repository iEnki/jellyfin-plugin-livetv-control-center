using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http.Extensions;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Jellyfin.Plugin.LiveTvGroups.Api;
/// <summary>Checks authenticated identities and canonical sources before native actions; resources remain tracked through streaming.</summary>
public class ChannelAccessFilter(ChannelAccessService access, ChannelAccessTagBridge bridge, ChannelAccessRevoker revoker,
    IServiceProvider services, ILogger<ChannelAccessFilter> logger) : IAsyncActionFilter, IAsyncResourceFilter, IOrderedFilter
{
    public int Order => -1000;
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        try { await next().ConfigureAwait(false); }
        finally { revoker.Complete(context.HttpContext); }
    }
    private static object? Field(object? value, string name) => value?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(value);
    private static string? String(object? value) => value?.ToString();
    private static bool Id(object? value, out Guid id) => Guid.TryParse(String(value), out id) && id != Guid.Empty;
    public static IReadOnlyList<Guid> TokenItems(string token, Guid userId)
    {
        var result = new HashSet<Guid>();
        foreach (Match match in Regex.Matches(token, @"(?:^|_)LiveTvChannel_([0-9a-fA-F]{32})(?:_|$)")) result.Add(Guid.ParseExact(match.Groups[1].Value, "N"));
        var encoded = token.Contains('_', StringComparison.Ordinal) ? token[(token.IndexOf('_', StringComparison.Ordinal) + 1)..] : token;
        try
        {
            using var json = JsonDocument.Parse(Convert.FromBase64String(encoded));
            if (json.RootElement.TryGetProperty("ExternalId", out var external) && GroupItemId.TryParse(external.GetString(), out _, out var source))
            {
                if (!json.RootElement.TryGetProperty("UserId", out var owner) || owner.GetGuid() != userId) throw new UnauthorizedAccessException("Stream token belongs to another user.");
                result.Add(source);
                if (json.RootElement.TryGetProperty("NativeToken", out var native))
                    foreach (var item in TokenItems(native.GetString()!, userId)) result.Add(item);
                if (result.Count != 1) throw new UnauthorizedAccessException("Contradictory stream identities.");
            }
        }
        catch (FormatException) { }
        catch (JsonException) { }
        return result.ToList();
    }
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var controller = descriptor?.ControllerName ?? String(context.RouteData.Values.GetValueOrDefault("controller")) ?? "";
        if (controller is "ChannelAccess" || !Regex.IsMatch(controller, "Groups|Items|Library|ItemLookup|ItemRefresh|ItemUpdate|Image|Trickplay|Subtitle|Lyrics|Video|Audio|Hls|MediaInfo|MediaSegments|LiveTv|Session|SyncPlay|Playlist|Search|InstantMix|Similar|Filter|TvShows", RegexOptions.CultureInvariant)) { await next().ConfigureAwait(false); return; }
        var http = context.HttpContext;
        var policyRevision = access.Configuration.PolicyRevision;
        try { if (!Regex.IsMatch(controller, "Video|Audio|Hls|MediaInfo|Image", RegexOptions.CultureInvariant)) await bridge.EnsureAsync(http.RequestAborted).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        { logger.LogError(error, "Channel access synchronization failed before native API action."); context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable); return; }
        if (!access.Configuration.Enabled) { await next().ConfigureAwait(false); return; }
        var values = new Dictionary<string, object?>(context.ActionArguments, StringComparer.OrdinalIgnoreCase);
        foreach (var value in context.ActionArguments.Values.Where(v => v is not null && v is not string && v is not Guid && v is not IEnumerable))
            foreach (var property in value!.GetType().GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
                values.TryAdd(property.Name, property.GetValue(value));
        foreach (var value in context.ActionArguments.Values)
            if (Field(value, "Arguments") is IDictionary<string, string> arguments)
                foreach (var argument in arguments) values.TryAdd(argument.Key, argument.Value);
        object? Value(string name) => values.GetValueOrDefault(name) ?? String(http.Request.Query[name]).NullIfEmpty();
        var users = services.GetRequiredService<IUserManager>();
        var library = services.GetRequiredService<ILibraryManager>();
        var caller = Id(http.User.FindFirst("Jellyfin-UserId")?.Value, out var callerId) ? users.GetUserById(callerId) : null;
        var fileId = String(Value("playlistId")) ?? String(Value("segmentId"));
        var inherited = controller == "HlsSegment" ? revoker.File(fileId) : null;
        if (access.Configuration.Enabled && inherited is not null && !revoker.Allowed(inherited)) { context.Result = new NotFoundResult(); return; }
        if (caller is null && inherited is not null) caller = users.GetUserById(inherited.UserId);
        // Legacy segment actions ignore the route item ID. Unowned files must never be treated as an allowed movie.
        if (access.Configuration.Enabled && controller == "HlsSegment" && fileId is not null && inherited is null) { context.Result = new NotFoundResult(); return; }
        if (access.Configuration.Enabled && caller is null && controller == "LiveTv" && descriptor?.ActionName is "GetLiveRecordingFile" or "GetLiveStreamFile"
            && http.User.FindFirst("Jellyfin-IsApiKey")?.Value != "True")
        { context.Result = new NotFoundResult(); return; }
        if (caller is null && http.User.FindFirst("Jellyfin-IsApiKey")?.Value != "True"
            && descriptor?.ActionName is "GetSubtitle" or "GetSubtitleWithTicks" or "GetAttachment")
        { context.Result = new NotFoundResult(); return; }
        if (caller is null) { await next().ConfigureAwait(false); return; } // Core authentication still controls other actions/API keys.
        // Legacy packed streaming options override the corresponding public query fields in Jellyfin.
        if (String(Value("params")) is { } packed)
        {
            var parts = packed.Split(';');
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])) values["deviceId"] = parts[1];
            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2])) values["mediaSourceId"] = parts[2];
        }
        SessionInfo? controlledSession = null;
        var targets = new List<Jellyfin.Database.Implementations.Entities.User> { caller };
        if (Id(Value("userId"), out var targetId) && targetId != caller.Id)
        {
            var target = users.GetUserById(targetId);
            if (target is not null) targets.Add(target);
        }
        if (controller == "Session" && Value("sessionId") is { } sessionId)
        {
            var session = services.GetRequiredService<ISessionManager>().Sessions.FirstOrDefault(s => s.Id == String(sessionId));
            controlledSession = session;
            var target = session is null ? null : users.GetUserById(session.UserId);
            if (target is not null) targets.Add(target);
        }
        var inventory = access.AllChannels();
        var ids = new HashSet<Guid>();
        foreach (var name in new[] { "id", "itemId", "channelId", "programId", "recordingId", "parentId", "routeItemId", "videoId" }) if (Id(Value(name), out var id)) ids.Add(id);
        if (Value("playingQueue") is IEnumerable<Guid> playingQueue) ids.UnionWith(playingQueue);
        if (Value("itemIds") is IEnumerable<Guid> itemIds) ids.UnionWith(itemIds);
        else if (Value("itemIds") is { } text) foreach (var part in String(text)!.Split(',')) if (Id(part, out var id)) ids.Add(id);
        if (controlledSession is not null)
        {
            var command = String(Field(Value("command"), "Name")) ?? String(Value("command"));
            if (command is "Unpause" or "PlayPause" or "Seek" or "NextTrack" or "PreviousTrack")
            {
                if (controlledSession.FullNowPlayingItem is { } playing) ids.Add(playing.Id);
                else if (controlledSession.NowPlayingItem is { } dto) ids.Add(dto.Id);
                // Native next/previous commands can select queued items without an explicit itemIds parameter.
                if (command is "NextTrack" or "PreviousTrack") ids.UnionWith(controlledSession.NowPlayingQueue.Select(q => q.Id));
            }
        }
        if (inherited is not null) ids.Add(inherited.ItemId);
        var playSessionId = String(Value("playSessionId"));
        var job = revoker.Job(playSessionId);
        if (job is not null) { ids.Add(job.ItemId); if (job.UserId != caller.Id) { context.Result = new NotFoundResult(); return; } }
        var openToken = String(Value("openToken"));
        if (openToken is not null)
        {
            try { ids.UnionWith(TokenItems(openToken, caller.Id)); }
            catch (UnauthorizedAccessException) { context.Result = new NotFoundResult(); return; }
        }
        if ((String(Value("mediaSourceId")) ?? String(Value("routeMediaSourceId"))) is { } mediaId && revoker.Stream(mediaId) is { } mediaItem) ids.Add(mediaItem);
        var streamId = String(Value("liveStreamId")) ?? String(Value("streamId"));
        if (streamId is not null)
        {
            var mapped = revoker.Stream(streamId);
            if (mapped is { } source) ids.Add(source);
            var stream = services.GetService<IMediaSourceManager>()?.GetLiveStreamInfo(streamId)
                ?? services.GetService<IMediaSourceManager>()?.GetLiveStreamInfoByUniqueId(streamId);
            if (access.Configuration.Enabled && mapped is null && string.IsNullOrEmpty(stream?.MediaSource.OpenToken))
            { context.Result = new NotFoundResult(); return; }
            if (stream?.MediaSource.OpenToken is { } token)
                try { ids.UnionWith(TokenItems(token, caller.Id)); }
                catch (UnauthorizedAccessException) { context.Result = new NotFoundResult(); return; }
        }
        // Active recordings use a timer/service string rather than their library item GUID.
        if (access.Configuration.Enabled && controller == "LiveTv" && descriptor?.ActionName == "GetLiveRecordingFile" && String(Value("recordingId")) is { } activeId)
        {
            var path = services.GetRequiredService<IRecordingsManager>().GetActiveRecordingPath(activeId);
            var recording = string.IsNullOrEmpty(path) ? null : library.FindByPath(path, false);
            if (recording is not null) ids.Add(recording.Id);
            else
            {
                try { var timer = await services.GetRequiredService<ILiveTvManager>().GetTimer(activeId, http.RequestAborted).ConfigureAwait(false); ids.Add(timer.ChannelId); }
                catch (Exception error) when (error is not OperationCanceledException) { context.Result = new NotFoundResult(); return; }
                if (!ids.Any(id => inventory.Any(c => c.Id == id))) { context.Result = new NotFoundResult(); return; }
            }
        }
        if (controller == "LiveTv" && String(Value("timerId")) is { } timerId)
        {
            var tv = services.GetRequiredService<ILiveTvManager>();
            if (descriptor?.ActionName.Contains("SeriesTimer", StringComparison.Ordinal) == true)
            { var timer = await tv.GetSeriesTimer(timerId, http.RequestAborted).ConfigureAwait(false); if (access.Configuration.Enabled && !TimerAllowed(caller, timer, inventory)) { context.Result = new NotFoundResult(); return; } if (timer.ChannelId != Guid.Empty) ids.Add(timer.ChannelId); }
            else { var timer = await tv.GetTimer(timerId, http.RequestAborted).ConfigureAwait(false); if (access.Configuration.Enabled && !TimerAllowed(caller, timer, inventory)) { context.Result = new NotFoundResult(); return; } if (timer.ChannelId != Guid.Empty) ids.Add(timer.ChannelId); }
        }
        var canonical = ids.Select(library.GetItemById).Where(i => i is not null).ToList();
        if (canonical.Any(item => targets.Any(u => !access.ItemAllowed(u, item!, inventory)))) { context.Result = new NotFoundResult(); return; }
        if (access.Configuration.Enabled)
        {
            var protectedIds = canonical.Where(i => access.ItemChannels(i!, inventory).Count > 0).Select(i => i!.Id).ToArray();
            if (protectedIds.Length > 0 && targets.Any(u => library.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery(ChannelAccessController.BaseRightsUser(u))
            { ItemIds = protectedIds, Recursive = true }).Select(i => i.Id).Intersect(protectedIds).Count() != protectedIds.Length))
            { context.Result = new NotFoundResult(); return; }
        }
        if (openToken is not null && ids.Count > 1 && canonical.SelectMany(i => access.ItemChannels(i!, inventory)).Select(c => c.Id).Distinct().Count() > 1)
        { context.Result = new BadRequestObjectResult("Contradictory stream identities."); return; }
        if (controller == "LiveTv" && descriptor?.ActionName.Contains("SeriesTimer", StringComparison.Ordinal) == true && http.Request.Method is "POST" or "PUT"
            && !Id(Value("channelId"), out _) && inventory.Any(c => !access.Allowed(caller, c, inventory)))
        { context.Result = new BadRequestObjectResult("Choose an authorized channel for the series recording."); return; }
        var tracked = canonical.FirstOrDefault(i => access.ItemChannels(i!, inventory).Count > 0) ?? canonical.FirstOrDefault();
        // Prefer the canonical TV source over unrelated query IDs, and retain ownership for ordinary movie HLS jobs.
        ChannelAccessRevoker.Lease? lease = tracked is null ? null : new(caller.Id, tracked.Id,
            http.User.FindFirst("Jellyfin-DeviceId")?.Value ?? String(Value("deviceId")), playSessionId, streamId);
        if (lease is not null) revoker.Track(http, lease);
        var executed = await next().ConfigureAwait(false);
        if (executed.Exception is not null && !executed.ExceptionHandled) return;
        if (access.Configuration.Enabled && canonical.Any(item => targets.Any(u => !access.ItemAllowed(u, item!, access.AllChannels()))))
        { executed.Result = new NotFoundResult(); return; }
        var result = (executed.Result as ObjectResult)?.Value;
        var latestInventory = access.Configuration.Enabled ? access.AllChannels() : inventory;
        if (access.Configuration.Enabled && http.Request.Method == "GET" && result is not null && controller != "Groups"
            && (policyRevision != access.Configuration.PolicyRevision || ForbiddenResult(result, targets, library, latestInventory)))
        {
            // A refresh/import may race the SQL tag projection. Never return a partially protected native page.
            bridge.Invalidate(); executed.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable); return;
        }
        // Native timer lists are not user-filtered by Jellyfin; unlike item queries they are returned without pagination.
        if (access.Configuration.Enabled && result is QueryResult<TimerInfoDto> timers)
        { var allowed = timers.Items.Where(t => TimerAllowed(caller, t, inventory)).ToList(); executed.Result = new OkObjectResult(new QueryResult<TimerInfoDto>(allowed)); }
        if (access.Configuration.Enabled && result is QueryResult<SeriesTimerInfoDto> series)
        { var allowed = series.Items.Where(t => TimerAllowed(caller, t, inventory)).ToList(); executed.Result = new OkObjectResult(new QueryResult<SeriesTimerInfoDto>(allowed)); }
        if (lease is not null)
        {
            var generated = String(Field(result, "PlaySessionId"));
            if (!string.IsNullOrEmpty(generated)) lease = lease with { PlaySessionId = generated };
            revoker.Track(http, lease);
            revoker.AssociateJob(lease);
            var media = Field(result, "MediaSource");
            if (media is not null) revoker.AssociateStream(lease, String(Field(media, "LiveStreamId")), String(Field(media, "Id")));
            if (Field(result, "MediaSources") is IEnumerable sources)
                foreach (var source in sources) revoker.AssociateStream(lease, String(Field(source, "LiveStreamId")), String(Field(source, "Id")));
        }
        if (access.Configuration.Enabled) ProtectResult(result, tracked?.Id, http, playSessionId, latestInventory);
    }
    private bool ForbiddenResult(object result, IReadOnlyList<Jellyfin.Database.Implementations.Entities.User> users, ILibraryManager library, IReadOnlyList<LiveTvChannel> inventory)
    {
        if (result is BaseItemDto item && library.GetItemById(item.Id) is { } canonical)
            return users.Any(u => !access.ItemAllowed(u, canonical, inventory));
        return Field(result, "Items") is IEnumerable items && items.Cast<object>().Any(i => ForbiddenResult(i, users, library, inventory));
    }
    private bool TimerAllowed(Jellyfin.Database.Implementations.Entities.User user, object timer, IReadOnlyList<LiveTvChannel> inventory)
    {
        if (user.HasPermission(PermissionKind.IsAdministrator)) return true;
        var id = Id(Field(timer, "ChannelId"), out var channelId) ? channelId : Guid.Empty;
        var reference = new Jellyfin.Plugin.LiveTvGroups.Model.ChannelRef
        {
            ItemId = id,
            ServiceName = String(Field(timer, "ServiceName")),
            ExternalId = String(Field(timer, "ExternalChannelId"))
        };
        var channels = ChannelAccessService.Resolve(reference, inventory);
        if (channels.Count > 0) return channels.All(c => access.Allowed(user, c, inventory));
        if (id == Guid.Empty && string.IsNullOrEmpty(reference.ExternalId)) return inventory.All(c => access.Allowed(user, c, inventory));
        return !access.Configuration.Rules.Any(rule => rule.Channels.Any(r => ChannelAccessService.SameReference(r, reference))
            && (rule.DeniedUserIds.Contains(user.Id) || !rule.VisibleToAllUsers && !rule.AllowedUserIds.Contains(user.Id)));
    }
    private void ProtectResult(object? result, Guid? itemId, HttpContext http, string? playSessionId, IReadOnlyList<LiveTvChannel> inventory)
    {
        if (result is BaseItemDto dto) itemId = dto.Id;
        if (Field(result, "Items") is IEnumerable items)
            foreach (var item in items) ProtectResult(item, null, http, playSessionId, inventory);
        if (itemId is not { } id || services.GetRequiredService<ILibraryManager>().GetItemById(id) is not { } source
            || access.ItemChannels(source, inventory).Count == 0) return;
        MediaSourceInfo Protect(MediaSourceInfo media)
        {
            var clone = JsonSerializer.Deserialize<MediaSourceInfo>(JsonSerializer.Serialize(media))!;
            if (!Uri.TryCreate(clone.Path, UriKind.Absolute, out var path) || path.Scheme is not ("http" or "https" or "rtsp" or "rtmp")) return clone;
            // Keep native format/direct-play negotiation, but deliver bytes through Jellyfin's authenticated proxy.
            // Never mutate the shared tuner's original MediaSourceInfo.
            var token = http.Request.Query["api_key"].ToString();
            if (string.IsNullOrEmpty(token)) token = http.User.FindFirst("Jellyfin-Token")?.Value;
            if (string.IsNullOrEmpty(token)) token = Regex.Match(http.Request.Headers.Authorization.ToString(), "Token=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
            var query = new Dictionary<string, string?>
            {
                ["Static"] = "true",
                ["MediaSourceId"] = clone.Id,
                ["LiveStreamId"] = clone.LiveStreamId,
                ["PlaySessionId"] = playSessionId,
                ["api_key"] = token
            };
            var proxied = UriHelper.BuildAbsolute(http.Request.Scheme, http.Request.Host, http.Request.PathBase,
                new PathString($"/Videos/{id:N}/stream"), QueryString.Create(query.Where(p => !string.IsNullOrEmpty(p.Value))));
            if (path.Scheme is "rtsp" or "rtmp")
            {
                // These protocols need Jellyfin remuxing; never expose the provider URL to clients.
                clone.Path = null; clone.SupportsDirectPlay = false; clone.SupportsDirectStream = false;
                clone.TranscodingUrl = proxied.Replace("Static=true", "Static=false", StringComparison.Ordinal) + "&VideoCodec=copy&AudioCodec=copy&Container=ts";
                clone.TranscodingContainer = "ts";
                clone.TranscodingSubProtocol = Jellyfin.Data.Enums.MediaStreamProtocol.http;
            }
            else clone.Path = proxied;
            clone.RequiredHttpHeaders.Clear();
            return clone;
        }
        var type = result?.GetType();
        if (Field(result, "MediaSource") is MediaSourceInfo single && type?.GetProperty("MediaSource") is { CanWrite: true } one) one.SetValue(result, Protect(single));
        if (Field(result, "MediaSources") is IEnumerable<MediaSourceInfo> sources && type?.GetProperty("MediaSources") is { CanWrite: true } many)
        {
            var protectedSources = sources.Select(Protect).ToArray();
            many.SetValue(result, many.PropertyType.IsArray ? protectedSources : protectedSources.ToList());
        }
    }
}
internal static class ChannelAccessStrings
{
    public static string? NullIfEmpty(this string? value) => string.IsNullOrEmpty(value) ? null : value;
}
