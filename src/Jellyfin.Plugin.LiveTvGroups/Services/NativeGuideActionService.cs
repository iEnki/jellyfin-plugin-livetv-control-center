using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Handles explicit native guide-folder visits without relying on provider cache execution.</summary>
public sealed class NativeGuideActionService(GroupService groups, IServiceProvider services,
    ILogger<NativeGuideActionService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(Guid UserId, string DeviceId), (Guid GroupId, DateTimeOffset At, bool Sent)> _recent = [];

    public async Task<NativeGuideActionResult?> OpenAsync(HttpContext http, Guid parentId, Guid? requestedChannel)
    {
        if (http.User.Identity?.IsAuthenticated != true || http.User.FindFirst("Jellyfin-IsApiKey")?.Value != "False"
            || !PlayerService.IsAndroidTvClient(http.User.FindFirst("Jellyfin-Client")?.Value)) return null;
        var device = http.User.FindFirst("Jellyfin-DeviceId")?.Value;
        if (!Guid.TryParse(http.User.FindFirst("Jellyfin-UserId")?.Value, out var userId)
            || userId == Guid.Empty || string.IsNullOrWhiteSpace(device) || device.Length > 512) return null;
        var library = services.GetRequiredService<ILibraryManager>();
        var pluginId = GroupsChannel.GetInternalId(library);
        var parent = library.GetItemById(parentId);
        if (parent is not Folder || parent.ChannelId != pluginId
            || (requestedChannel is not null && requestedChannel != pluginId)
            || !NativeGuideActionRoute.TryParse(parent.ExternalId, out var groupId)) return null;
        var user = services.GetRequiredService<IUserManager>().GetUserById(userId);
        if (user is null || user.HasPermission(PermissionKind.IsDisabled) || !user.HasPermission(PermissionKind.EnableLiveTvAccess)) return null;
        var channelList = await services.GetRequiredService<IChannelManager>().GetChannelsInternalAsync(new ChannelQuery { UserId = userId }).ConfigureAwait(false);
        if (!channelList.Items.Any(c => c.Id == pluginId)) return null;
        var auth = await services.GetRequiredService<IAuthorizationContext>().GetAuthorizationInfo(http).ConfigureAwait(false);
        if (auth.IsApiKey || !auth.HasToken || auth.UserId != userId || auth.DeviceId != device) return null;
        var sessions = services.GetRequiredService<ISessionManager>();
        var caller = await sessions.GetSessionByAuthenticationToken(auth.Token, auth.DeviceId,
            http.Connection.RemoteIpAddress?.ToString()).ConfigureAwait(false);
        if (!OwnTvSession(caller, userId, device)) return null;
        var scopes = services.GetRequiredService<NativeGuideService>();
        await _gate.WaitAsync(http.RequestAborted).ConfigureAwait(false);
        try
        {
            user = services.GetRequiredService<IUserManager>().GetUserById(userId);
            if (user is null || user.HasPermission(PermissionKind.IsDisabled) || !user.HasPermission(PermissionKind.EnableLiveTvAccess)) return null;
            var group = groupId is null ? null : groups.GetGroups(user).FirstOrDefault(g => g.Id == groupId);
            if (groupId is not null && (group is null || scopes.Resolve(user, groupId.Value) is null)) return null;
            var key = (userId, device);
            if (groupId is not null && _recent.TryGetValue(key, out var previous) && previous.GroupId == groupId
                && DateTimeOffset.UtcNow - previous.At < TimeSpan.FromSeconds(3) && scopes.Get(userId, device) == groupId)
                return new(groupId, group?.Name, previous.Sent);

            if (groupId is null) { scopes.Clear(userId, device); _recent.Remove(key); }
            else scopes.Set(user, device, groupId.Value);
            logger.LogInformation("Native TV guide action for user {UserId}, device {DeviceId}, group {GroupId}.", userId, device, groupId);

            // Resolve the caller again: a logout/reconnect must never target a replacement session/user.
            var current = sessions.Sessions.FirstOrDefault(s => s.Id == caller!.Id);
            var sent = false;
            if (OwnTvSession(current, userId, device) && current!.IsActive && current.SessionControllers.Any(c => c.IsSessionActive) && current.NowPlayingItem is null
                && current.Capabilities.SupportedCommands.Contains(GeneralCommandType.DisplayContent))
            {
                try
                {
                    var view = services.GetRequiredService<ILiveTvManager>().GetInternalLiveTvFolder(http.RequestAborted);
                    if (view is UserView { CollectionType: CollectionType.livetv } && view.Id != Guid.Empty)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        await sessions.SendGeneralCommand(current.Id, current.Id, new GeneralCommand(new()
                        {
                            ["ItemId"] = view.Id.ToString("N"), ["ItemType"] = "UserView"
                        }) { Name = GeneralCommandType.DisplayContent, ControllingUserId = userId }, timeout.Token).ConfigureAwait(false);
                        sent = true;
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException || !http.RequestAborted.IsCancellationRequested)
                { logger.LogWarning(error, "Native guide selected, but Live TV navigation was unavailable; open Live TV / TV Guide manually."); }
            }
            // A successful socket send is not proof that the client rendered/navigated the view.
            if (groupId is not null)
            {
                if (_recent.Count >= 256) _recent.Remove(_recent.MinBy(p => p.Value.At).Key);
                _recent[key] = (groupId.Value, DateTimeOffset.UtcNow, sent);
            }
            return new(groupId, group?.Name, sent);
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();

    private static bool OwnTvSession(SessionInfo? session, Guid userId, string device)
        => session is not null && !string.IsNullOrEmpty(session.Id) && session.UserId == userId && session.DeviceId == device
            && PlayerService.IsAndroidTv(session);
}

public sealed record NativeGuideActionResult(Guid? GroupId, string? GroupName, bool NavigationCommandSent);
