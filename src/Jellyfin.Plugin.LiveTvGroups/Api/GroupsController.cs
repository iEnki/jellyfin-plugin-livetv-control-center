using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Web;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LiveTvGroups.Api;

/// <summary>
/// REST API for the groups of the current user.
/// </summary>
[ApiController]
[Route("LiveTvGroups")]
public class GroupsController : ControllerBase
{
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly GroupService _groups;
    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;
    private readonly WebInjectionStatus _injectionStatus;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupsController"/> class.
    /// </summary>
    /// <param name="groups">Group service.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="dtoService">DTO service.</param>
    /// <param name="injectionStatus">Web injection status.</param>
    public GroupsController(GroupService groups, IUserManager userManager, IDtoService dtoService, WebInjectionStatus injectionStatus)
    {
        _groups = groups;
        _userManager = userManager;
        _dtoService = dtoService;
        _injectionStatus = injectionStatus;
    }

    /// <summary>
    /// Gets the groups of the current user.
    /// </summary>
    /// <returns>The groups.</returns>
    [HttpGet("Groups")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult<IEnumerable<GroupDto>> GetGroups()
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(_groups.Store.Get(user.Id).Groups.Select(ToDto));
    }

    /// <summary>
    /// Creates a group.
    /// </summary>
    /// <param name="request">Group name.</param>
    /// <returns>The created group.</returns>
    [HttpPost("Groups")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult<GroupDto> CreateGroup([FromBody, Required] GroupNameRequest request)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return BadRequest("Name fehlt.");
        }

        var group = _groups.Store.Update(user.Id, doc =>
        {
            var created = new ChannelGroup { Id = Guid.NewGuid(), Name = name };
            doc.Groups.Add(created);
            return created;
        });

        return Ok(ToDto(group));
    }

    /// <summary>
    /// Renames a group.
    /// </summary>
    /// <param name="groupId">Group id.</param>
    /// <param name="request">New name.</param>
    /// <returns>No content.</returns>
    [HttpPut("Groups/{groupId}")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult RenameGroup([FromRoute] Guid groupId, [FromBody, Required] GroupNameRequest request)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return BadRequest("Name fehlt.");
        }

        var found = _groups.Store.Update(user.Id, doc =>
        {
            var group = doc.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group is not null)
            {
                group.Name = name;
            }

            return group is not null;
        });

        return found ? NoContent() : NotFound();
    }

    /// <summary>
    /// Deletes a group.
    /// </summary>
    /// <param name="groupId">Group id.</param>
    /// <returns>No content.</returns>
    [HttpDelete("Groups/{groupId}")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult DeleteGroup([FromRoute] Guid groupId)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var removed = _groups.Store.Update(user.Id, doc => doc.Groups.RemoveAll(g => g.Id == groupId) > 0);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Sets the order of the groups. Groups missing in the list keep their relative order at the end.
    /// </summary>
    /// <param name="groupIds">Group ids in the desired order.</param>
    /// <returns>No content.</returns>
    [HttpPut("Groups/Order")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult SetGroupOrder([FromBody, Required] Guid[] groupIds)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        _groups.Store.Update(user.Id, doc =>
        {
            var position = groupIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            doc.Groups = doc.Groups
                .OrderBy(g => position.TryGetValue(g.Id, out var index) ? index : int.MaxValue)
                .ToList();
            return true;
        });

        return NoContent();
    }

    /// <summary>
    /// Gets the channels of a group including the current program.
    /// </summary>
    /// <param name="groupId">Group id.</param>
    /// <returns>The channels.</returns>
    [HttpGet("Groups/{groupId}/Channels")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult<QueryResult<BaseItemDto>> GetGroupChannels([FromRoute] Guid groupId)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var group = _groups.Store.Get(user.Id).Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null)
        {
            return NotFound();
        }

        var channels = _groups.ResolveChannels(user, group, _groups.GetAccessibleChannels(user));
        var options = new DtoOptions(false)
        {
            AddCurrentProgram = true,
            EnableImages = true,
            ImageTypeLimit = 1,
            ImageTypes = [ImageType.Primary],
            EnableUserData = true
        };

        var items = _dtoService.GetBaseItemDtos(channels.Cast<MediaBrowser.Controller.Entities.BaseItem>().ToList(), options, user);
        return Ok(new QueryResult<BaseItemDto>(0, items.Count, items));
    }

    /// <summary>
    /// Sets the channels of a group.
    /// </summary>
    /// <param name="groupId">Group id.</param>
    /// <param name="channelIds">Channel item ids in display order.</param>
    /// <returns>No content.</returns>
    [HttpPut("Groups/{groupId}/Channels")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult SetGroupChannels([FromRoute] Guid groupId, [FromBody, Required] Guid[] channelIds)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var accessible = _groups.GetAccessibleChannels(user);
        if (channelIds.Any(id => !accessible.ContainsKey(id)))
        {
            return BadRequest("Unbekannter oder nicht erlaubter Sender.");
        }

        var refs = channelIds.Distinct().Select(id => GroupService.ToRef(accessible[id])).ToList();
        var found = _groups.Store.Update(user.Id, doc =>
        {
            var group = doc.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group is not null)
            {
                group.Channels = refs;
            }

            return group is not null;
        });

        return found ? NoContent() : NotFound();
    }

    /// <summary>
    /// Gets the plugin status for the dashboard page.
    /// </summary>
    /// <returns>The status.</returns>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<WebInjectionStatus> GetStatus() => Ok(_injectionStatus);

    /// <summary>
    /// Serves the web client script.
    /// </summary>
    /// <returns>The script.</returns>
    [HttpGet("client.js")]
    [AllowAnonymous]
    public ActionResult GetClientScript() => ServeResource("client.js", "application/javascript");

    /// <summary>
    /// Serves the web client stylesheet.
    /// </summary>
    /// <returns>The stylesheet.</returns>
    [HttpGet("client.css")]
    [AllowAnonymous]
    public ActionResult GetClientStyles() => ServeResource("client.css", "text/css");

    private static GroupDto ToDto(ChannelGroup group) => new(group.Id, group.Name, group.Channels.Count);

    private ActionResult ServeResource(string name, string contentType)
    {
        var stream = typeof(GroupsController).Assembly.GetManifestResourceStream("Jellyfin.Plugin.LiveTvGroups.Web." + name);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return File(stream, contentType + "; charset=utf-8");
    }

    private User? GetUser()
    {
        var claim = User.FindFirst(UserIdClaim)?.Value;
        return Guid.TryParse(claim, out var userId) ? _userManager.GetUserById(userId) : null;
    }
}

/// <summary>
/// Group summary.
/// </summary>
/// <param name="Id">Group id.</param>
/// <param name="Name">Group name.</param>
/// <param name="ChannelCount">Number of stored channels.</param>
public record GroupDto(Guid Id, string Name, int ChannelCount);

/// <summary>
/// Request body with a group name.
/// </summary>
public class GroupNameRequest
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }
}
