using System;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks = new(StringComparer.Ordinal);

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
        cancellationToken.ThrowIfCancellationRequested();
        var gate = _deviceLocks.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await PlayCore(user, caller, deviceId, channelId, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task PlayCore(User user, SessionInfo caller, string deviceId, Guid channelId, CancellationToken cancellationToken)
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
            throw new PlayerUnavailableException("Target unavailable. Open Jellyfin on the TV and refresh devices.");
        }

        var targetUser = _users.GetUserById(target.UserId);
        if (targetUser is null) { throw new SecurityException("Kein angemeldeter Benutzer am Zielgerät."); }
        RequirePlaybackAccess(targetUser);
        RequireChannelAccess(user, targetUser, channelId);

        // Android TV replaces the player route on PlayNow. Wait for the old player to close
        // before navigating to another one, otherwise its cleanup can pop the new route.
        if (IsAndroidTv(target) && target.NowPlayingItem is not null)
        {
            var sessionId = target.Id; var owner = target.UserId;
            await _sessions.SendPlaystateCommand(caller.Id, sessionId,
                new PlaystateRequest { Command = PlaystateCommand.Stop }, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; ; attempt++)
            {
                target = EligibleSessions(user).FirstOrDefault(s => s.DeviceId == deviceId && s.Id == sessionId && s.UserId == owner)
                    ?? throw new PlayerUnavailableException("TV session changed while switching. Refresh devices.");
                if (target.NowPlayingItem is null) { break; }
                if (attempt >= 50) { throw new PlayerUnavailableException("TV did not confirm playback stopped. Please try again."); }
                await WaitForPlayer(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }

            // PlaybackStopped is sent before Android finishes disposing the player route.
            await WaitForPlayer(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            target = EligibleSessions(user).FirstOrDefault(s => s.DeviceId == deviceId && s.Id == sessionId && s.UserId == owner)
                ?? throw new PlayerUnavailableException("TV session is no longer available.");
            if (target.NowPlayingItem is not null) { throw new PlayerUnavailableException("Another playback has already started on the TV."); }
            user = _users.GetUserById(user.Id) ?? throw new SecurityException("Der aufrufende Benutzer ist nicht mehr verfügbar.");
            var currentTargetUser = _users.GetUserById(owner) ?? throw new SecurityException("Kein angemeldeter Benutzer am Zielgerät.");
            RequirePlaybackAccess(user); RequirePlaybackAccess(currentTargetUser);
            if (owner != user.Id && !user.HasPermission(PermissionKind.EnableRemoteControlOfOtherUsers))
            { throw new SecurityException("Die Fernsteuerungsberechtigung wurde während des Wechsels geändert."); }
            RequireChannelAccess(user, currentTargetUser, channelId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _sessions.SendPlayCommand(caller.Id, target.Id,
            new PlayRequest { PlayCommand = PlayCommand.PlayNow, ItemIds = [channelId], StartPositionTicks = 0, ControllingUserId = user.Id },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Waits for TV playback reporting and player disposal. Virtual for deterministic transport tests.</summary>
    protected virtual Task WaitForPlayer(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);

    private void RequireChannelAccess(User user, User targetUser, Guid channelId)
    {
        if (!_groups.GetAccessibleChannels(user).TryGetValue(channelId, out var channel)
            || !_groups.GetAccessibleChannels(targetUser).TryGetValue(channelId, out var targetChannel)
            || channel.GetPlayAccess(user) != PlayAccess.Full || targetChannel.GetPlayAccess(targetUser) != PlayAccess.Full
            || (_groups.Shared && (!HasGroupChannel(user, channelId) || !HasGroupChannel(targetUser, channelId))))
        { throw new SecurityException("Dieser Sender darf von einem der Benutzer nicht abgespielt werden oder gehört zu keiner freigegebenen Gruppe."); }
    }

    private bool HasGroupChannel(User user, Guid channelId)
    {
        var accessible = _groups.GetAccessibleChannels(user);
        return _groups.GetGroups(user).Any(g => _groups.ResolveChannels(user, g, accessible).Any(c => c.Id == channelId));
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
