using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LiveTvGroups.Api;

/// <summary>Player operations for the authenticated user; no anonymous or privileged API-key control.</summary>
[ApiController]
[Route("LiveTvGroups/Players")]
[Authorize(Policy = Policies.LiveTvAccess)]
public class PlayersController(
    PlayerService players,
    GroupService groups,
    IUserManager users,
    ISessionManager sessions,
    IAuthorizationContext authorization) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<PlayerDto>> GetPlayers()
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        try { return Ok(players.GetPlayers(user)); }
        catch (SecurityException) { return Forbid(); }
    }

    [HttpPut("Preference")]
    public ActionResult SetPreference([FromBody, Required] PlayerPreferenceRequest request)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        try
        {
            // Resolve the server-owned name, and validate authorization even for guessed DeviceIds.
            var target = request.DeviceId is null ? null : players.FindPlayer(user, request.DeviceId);
            if (request.DeviceId is not null && target is null) { return Conflict("Target unavailable or not allowed."); }
            groups.Store.Update(user.Id, doc =>
            {
                doc.Preferences.PreferredTargetDeviceId = target?.DeviceId;
                doc.Preferences.PreferredTargetDeviceName = target?.Name;
                return true;
            });
            return NoContent();
        }
        catch (SecurityException) { return Forbid(); }
    }

    [HttpPost("{deviceId}/Play/{channelId:guid}")]
    public async Task<ActionResult> Play([FromRoute] string deviceId, [FromRoute] Guid channelId)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        try
        {
            var auth = await authorization.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
            if (auth.IsApiKey || string.IsNullOrEmpty(auth.Token)) { return Unauthorized(); }
            var caller = await sessions.GetSessionByAuthenticationToken(auth.Token, auth.DeviceId,
                HttpContext.Connection.RemoteIpAddress?.ToString()).ConfigureAwait(false);
            if (caller is null) { return Conflict("No active controlling session. Please sign in again."); }
            await players.Play(user, caller, deviceId, channelId, HttpContext.RequestAborted).ConfigureAwait(false);
            return NoContent();
        }
        catch (SecurityException) { return Forbid(); }
        catch (PlayerUnavailableException e) { return Conflict(e.Message); }
        catch (ResourceNotFoundException) { return Conflict("TV session ended. Refresh devices and try again."); }
        catch (ArgumentException) { return BadRequest("Jellyfin could not play this channel on the target."); }
        catch (WebSocketException) { return StatusCode(502, "Target connection was interrupted."); }
        catch (IOException) { return StatusCode(502, "Could not send the playback command to the target."); }
    }

    private User? GetUser()
        => Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var id) ? users.GetUserById(id) : null;
}

public class PlayerPreferenceRequest
{
    [StringLength(512, MinimumLength = 1)]
    public string? DeviceId { get; set; }
}
