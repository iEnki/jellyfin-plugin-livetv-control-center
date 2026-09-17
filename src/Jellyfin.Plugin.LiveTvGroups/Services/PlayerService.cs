using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Discovers authorized players independently of Jellyfin's controllable-session filter.</summary>
public class PlayerService
{
    private readonly ISessionManager _sessions;
    private readonly IUserManager _users;
    private readonly GroupService _groups;

    public PlayerService(ISessionManager sessions, IUserManager users, GroupService groups)
    {
        _sessions = sessions;
        _users = users;
        _groups = groups;
    }

    public IReadOnlyList<PlayerDto> GetPlayers(User user)
    {
        RequirePlaybackAccess(user);
        return EligibleSessions(user)
            .GroupBy(s => s.DeviceId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(s => new PlayerDto(s.DeviceId, s.DeviceName, s.Client,
                IsAndroidTv(s) && !s.SupportsRemoteControl))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public PlayerDto? FindPlayer(User user, string deviceId)
        => GetPlayers(user).FirstOrDefault(p => string.Equals(p.DeviceId, deviceId, StringComparison.Ordinal));

    public async Task Play(User user, SessionInfo caller, string deviceId, Guid channelId, CancellationToken cancellationToken)
    {
        RequirePlaybackAccess(user);
        // Never pass an empty controllingSessionId: that is Jellyfin's privileged/API-key path.
        if (caller.UserId != user.Id || string.IsNullOrEmpty(caller.Id))
        {
            throw new SecurityException("Die aufrufende Sitzung gehört nicht zum angemeldeten Benutzer.");
        }

        var target = EligibleSessions(user).FirstOrDefault(s => string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal));
        if (target is null)
        {
            throw new PlayerUnavailableException("Das Zielgerät ist nicht verfügbar. Jellyfin am TV öffnen und Geräte aktualisieren.");
        }

        var targetUser = _users.GetUserById(target.UserId);
        if (targetUser is null) { throw new SecurityException("Kein angemeldeter Benutzer am Zielgerät."); }
        RequirePlaybackAccess(targetUser);
        if (!_groups.GetAccessibleChannels(user).TryGetValue(channelId, out var channel)
            || !_groups.GetAccessibleChannels(targetUser).TryGetValue(channelId, out var targetChannel)
            || channel.GetPlayAccess(user) != PlayAccess.Full
            || targetChannel.GetPlayAccess(targetUser) != PlayAccess.Full)
        {
            throw new SecurityException("Dieser Live-TV-Sender darf von einem der Benutzer nicht abgespielt werden.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _sessions.SendPlayCommand(caller.Id, target.Id,
            new PlayRequest { PlayCommand = PlayCommand.PlayNow, ItemIds = [channelId], ControllingUserId = user.Id },
            cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<SessionInfo> EligibleSessions(User user)
        => _sessions.Sessions
            .Where(s => !string.IsNullOrEmpty(s.DeviceId)
                && !string.IsNullOrEmpty(s.Id)
                && s.UserId != Guid.Empty
                && (s.UserId == user.Id || user.HasPermission(PermissionKind.EnableRemoteControlOfOtherUsers))
                // IsActive alone is true for sessions with NO controllers. Require a live message transport.
                && s.IsActive && s.SessionControllers.Any(c => c.IsSessionActive)
                && (s.SupportsRemoteControl || IsAndroidTv(s)))
            .OrderByDescending(s => s.LastActivityDate)
            .ThenBy(s => s.Id, StringComparer.Ordinal);

    internal static bool IsAndroidTv(SessionInfo session)
        // Fire TV runs the official Android TV client. Device names are user-editable and not evidence.
        => string.Equals(session.Client, "Jellyfin Android TV", StringComparison.OrdinalIgnoreCase);

    private static void RequirePlaybackAccess(User user)
    {
        if (user.HasPermission(PermissionKind.IsDisabled)
            || !user.HasPermission(PermissionKind.EnableLiveTvAccess)
            || !user.HasPermission(PermissionKind.EnableMediaPlayback))
        {
            throw new SecurityException("Keine Berechtigung für Live-TV-Wiedergabe.");
        }
    }
}

public record PlayerDto(string DeviceId, string Name, string Client, bool UsesAndroidTvDiscoveryFallback);
public class PlayerUnavailableException(string message) : Exception(message);
