using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Api;

/// <summary>Explicit native guide selection; no API keys, arbitrary user IDs or cross-user scope transfer.</summary>
[ApiController]
[Route("LiveTvGroups/NativeGuide")]
[Authorize(Policy = Policies.LiveTvAccess)]
public class NativeGuideController(NativeGuideService scopes, PlayerService players, IUserManager users,
    ILogger<NativeGuideController> logger) : ControllerBase
{
    [HttpGet]
    public ActionResult GetCurrent() => Read(User.FindFirst("Jellyfin-DeviceId")?.Value);

    [HttpGet("Devices/{deviceId}")]
    public ActionResult GetDevice([FromRoute, StringLength(512, MinimumLength = 1)] string deviceId) => Read(deviceId);

    [HttpPut]
    public ActionResult SetCurrent([FromBody] NativeGuideRequest request)
        => Set(User.FindFirst("Jellyfin-DeviceId")?.Value, request, currentDevice: true);

    [HttpPut("Devices/{deviceId}")]
    public ActionResult SetDevice([FromRoute, StringLength(512, MinimumLength = 1)] string deviceId,
        [FromBody] NativeGuideRequest request) => Set(deviceId, request, currentDevice: false);

    [HttpDelete]
    public ActionResult ClearCurrent() => Clear(User.FindFirst("Jellyfin-DeviceId")?.Value);

    [HttpDelete("Devices/{deviceId}")]
    public ActionResult ClearDevice([FromRoute, StringLength(512, MinimumLength = 1)] string deviceId) => Clear(deviceId);

    private User? Caller()
        => User.Identity?.IsAuthenticated == true
            && string.Equals(User.FindFirst("Jellyfin-IsApiKey")?.Value, "False", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var id) && id != Guid.Empty
            ? users.GetUserById(id) : null;

    private ActionResult Read(string? device)
    {
        var user = Caller();
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(device)) return BadRequest("Missing device identity.");
        try
        {
            var selection = scopes.GetSelection(user.Id, device);
            if (selection is not null && scopes.Resolve(user, selection) is null)
            { scopes.ClearSelection(user.Id, device, selection); selection = null; }
            return Ok(new { DeviceId = device, GroupId = selection?.GroupId, AllVisibleGroups = selection?.AllVisibleGroups == true, Enabled = selection is not null });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { return StorageFailure(error); }
    }

    private ActionResult Set(string? device, NativeGuideRequest request, bool currentDevice)
    {
        var user = Caller();
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(device) || device.Length > 512 || (request.AllVisibleGroups ? request.GroupId != Guid.Empty : request.GroupId == Guid.Empty))
            return BadRequest("Select exactly one group or all visible groups for a device.");
        try
        {
            if (currentDevice)
            {
                if (!PlayerService.IsNativeGuideClient(User.FindFirst("Jellyfin-Client")?.Value))
                    return BadRequest("Only a supported TV guide client can scope its current device.");
            }
            else if (!players.CanSetNativeGuide(user, device))
                return Conflict("Open a supported TV guide app on the target and sign in as the same user.");
            if (request.AllVisibleGroups) scopes.SetVisibleGroups(user, device);
            else scopes.Set(user, device, request.GroupId);
            return NoContent();
        }
        catch (NativeGuideGroupUnavailableException) { return NotFound("Selection unavailable or has no accessible group channels."); }
        catch (MediaBrowser.Controller.Net.SecurityException) { return Forbid(); }
        catch (Exception error) when (error is not OperationCanceledException) { return StorageFailure(error); }
    }

    private ActionResult Clear(string? device)
    {
        var user = Caller();
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(device)) return BadRequest("Missing device identity.");
        // Clearing is safe even after the user's device goes offline; no other user's data is touched.
        try { scopes.Clear(user.Id, device); return NoContent(); }
        catch (Exception error) when (error is not OperationCanceledException) { return StorageFailure(error); }
    }

    private ActionResult StorageFailure(Exception error)
    {
        logger.LogWarning(error, "Native guide scope operation could not be completed.");
        return StatusCode(503, "Native guide settings unavailable. Please try again.");
    }
}

public class NativeGuideRequest
{
    public Guid GroupId { get; set; }
    public bool AllVisibleGroups { get; set; }
}
