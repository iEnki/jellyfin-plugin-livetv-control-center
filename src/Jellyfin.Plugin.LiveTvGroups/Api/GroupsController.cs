using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Web;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LiveTvGroups.Api;

/// <summary>
/// REST API for the groups of the current user.
/// </summary>
[ApiController]
[Route("LiveTvGroups")]
public class GroupsController : Controller
{
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly GroupService _groups;
    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;
    private readonly WebInjectionStatus _injectionStatus;
    private readonly PlaylistSyncService _playlistSync;
    private readonly GroupArtworkService? _artwork;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupsController"/> class.
    /// </summary>
    /// <param name="groups">Group service.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="dtoService">DTO service.</param>
    /// <param name="injectionStatus">Web injection status.</param>
    /// <param name="playlistSync">Playlist sync service.</param>
    /// <param name="artwork">Group artwork service.</param>
    public GroupsController(
        GroupService groups,
        IUserManager userManager,
        IDtoService dtoService,
        WebInjectionStatus injectionStatus,
        PlaylistSyncService playlistSync,
        GroupArtworkService? artwork = null)
    {
        _playlistSync = playlistSync;
        _artwork = artwork;
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

        return Ok(_groups.GetGroups(user).Select(g => ToDto(g, user)));
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

        if (!_groups.CanManage(user)) { return Forbid(); }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            return BadRequest("Name is required.");
        }

        var group = _groups.Update(user, doc =>
        {
            var created = new ChannelGroup { Id = Guid.NewGuid(), Name = name };
            doc.Groups.Add(created);
            return created;
        });

        QueueGroupSync(user.Id);
        return Ok(ToDto(group, user));
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

        if (!_groups.CanManage(user)) { return Forbid(); }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            return BadRequest("Name is required.");
        }

        var found = _groups.Update(user, doc =>
        {
            var group = doc.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group is not null)
            {
                group.Name = name;
            }

            return group is not null;
        });

        QueueGroupSync(user.Id);
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

        if (!_groups.CanManage(user)) { return Forbid(); }

        var removed = _groups.Update(user, doc =>
        {
            doc.Preferences.HiddenGroupIds.Remove(groupId);
            if (doc.Preferences.DefaultGroupId == groupId) { doc.Preferences.DefaultGroupId = null; }
            if (doc.Preferences.LastGroupId == groupId) { doc.Preferences.LastGroupId = null; }
            return doc.Groups.RemoveAll(g => g.Id == groupId) > 0;
        });
        if (removed) { Artwork.DeleteCustomImage(user.Id, _groups.Shared, groupId); }
        QueueGroupSync(user.Id);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>Gets a group's custom image or the standard image.</summary>
    [HttpGet("Groups/{groupId}/Image")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult GetGroupImage([FromRoute] Guid groupId)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        var group = _groups.GetGroups(user).FirstOrDefault(candidate => candidate.Id == groupId);
        if (group is null) { return NotFound(); }
        var path = Artwork.GetGroupImage(user.Id, _groups.Shared, groupId);
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(path, GroupArtworkService.GetContentType(path), enableRangeProcessing: false);
    }

    /// <summary>Replaces a group's custom image.</summary>
    [HttpPut("Groups/{groupId}/Image")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    [RequestSizeLimit(GroupArtworkService.MaxImageBytes)]
    public async Task<ActionResult<GroupDto>> SetGroupImage([FromRoute] Guid groupId, CancellationToken cancellationToken)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        if (!_groups.CanManage(user)) { return Forbid(); }
        var shared = _groups.Shared;
        if (!_groups.GetGroups(user).Any(group => group.Id == groupId)) { return NotFound(); }
        if (Request.ContentLength > GroupArtworkService.MaxImageBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "The image must not exceed 5 MiB.");
        }

        string path;
        try
        {
            path = await Artwork.SaveCustomImageAsync(user.Id, shared, groupId, Request.Body, Request.ContentType, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException error)
        {
            return BadRequest(error.Message);
        }

        var updated = _groups.Update(user, document =>
        {
            var group = document.Groups.FirstOrDefault(candidate => candidate.Id == groupId);
            if (group is not null) { group.ArtworkRevision++; }
            return group;
        });
        if (updated is null)
        {
            Artwork.DeleteCustomImage(user.Id, shared, groupId);
            return NotFound();
        }

        await Artwork.UpdateCachedGroupImageAsync(groupId, path, cancellationToken).ConfigureAwait(false);
        return Ok(ToDto(updated, user));
    }

    /// <summary>Restores the standard image for a group.</summary>
    [HttpDelete("Groups/{groupId}/Image")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public async Task<ActionResult> ResetGroupImage([FromRoute] Guid groupId, CancellationToken cancellationToken)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        if (!_groups.CanManage(user)) { return Forbid(); }
        var shared = _groups.Shared;
        if (!_groups.GetGroups(user).Any(group => group.Id == groupId)) { return NotFound(); }

        var found = _groups.Update(user, document =>
        {
            var group = document.Groups.FirstOrDefault(candidate => candidate.Id == groupId);
            if (group is not null) { group.ArtworkRevision++; }
            return group is not null;
        });
        if (!found) { return NotFound(); }

        Artwork.DeleteCustomImage(user.Id, shared, groupId);
        await Artwork.UpdateCachedGroupImageAsync(groupId, Artwork.EpgPath, cancellationToken).ConfigureAwait(false);
        return NoContent();
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

        if (!_groups.CanManage(user)) { return Forbid(); }

        if (groupIds.Distinct().Count() != groupIds.Length) { return BadRequest("Duplicate group ID."); }

        _groups.Update(user, doc =>
        {
            var position = groupIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            doc.Groups = doc.Groups
                .OrderBy(g => position.TryGetValue(g.Id, out var index) ? index : int.MaxValue)
                .ToList();
            return true;
        });

        QueueGroupSync(user.Id);
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

        var group = _groups.GetGroups(user).FirstOrDefault(g => g.Id == groupId);
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

        if (!_groups.CanManage(user)) { return Forbid(); }

        var accessible = _groups.GetAccessibleChannels(user);
        if (channelIds.Any(id => !accessible.ContainsKey(id)))
        {
            return BadRequest("Unknown or forbidden channel.");
        }

        var refs = channelIds.Distinct().Select(id => GroupService.ToRef(accessible[id])).ToList();
        var found = _groups.Update(user, doc =>
        {
            var group = doc.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group is not null)
            {
                if (HttpContext.RequestServices.GetService<ChannelAccessService>() is { Configuration.Enabled: true } policy)
                {
                    var inventory = policy.AllChannels();
                    var pending = new Queue<ChannelRef>(refs); var preserved = new List<ChannelRef>();
                    foreach (var original in group.Channels)
                    {
                        if (!ChannelAccessService.Resolve(original, inventory).Any(c => accessible.ContainsKey(c.Id))) preserved.Add(original);
                        else if (pending.TryDequeue(out var selected)) preserved.Add(selected);
                    }
                    preserved.AddRange(pending);
                    group.Channels = preserved.DistinctBy(r => (r.ItemId, r.ServiceName, r.ExternalId)).ToList();
                }
                else group.Channels = refs;
            }

            return group is not null;
        });

        QueueGroupSync(user.Id);
        return found ? NoContent() : NotFound();
    }

    /// <summary>
    /// Gets the program guide of a group for a time window.
    /// </summary>
    /// <param name="groupId">Group id.</param>
    /// <param name="start">Window start (UTC); defaults to now.</param>
    /// <param name="end">Window end (UTC); defaults to start + 6 hours, at most 24 hours.</param>
    /// <returns>Channels and programs.</returns>
    [HttpGet("Groups/{groupId}/Guide")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public async Task<ActionResult<GuideDto>> GetGroupGuide([FromRoute] Guid groupId, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
    {
        var user = GetUser();
        if (user is null)
        {
            return Unauthorized();
        }

        var group = _groups.GetGroups(user).FirstOrDefault(g => g.Id == groupId);
        if (group is null)
        {
            return NotFound();
        }

        return Ok(await HttpContext.RequestServices.GetRequiredService<GroupGuideService>()
            .GetGuide(user, [group], start, end, HttpContext.RequestAborted).ConfigureAwait(false));
    }

    /// <summary>Gets personal preferences for the groups page.</summary>
    [HttpGet("Preferences")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult<GroupPreferences> GetPreferences()
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        var preferences = JsonSerializer.Deserialize<GroupPreferences>(JsonSerializer.SerializeToUtf8Bytes(_groups.Store.Get(user.Id).Preferences))!;
        var ids = _groups.GetGroups(user).Select(g => g.Id).ToHashSet();
        preferences.HiddenGroupIds.RemoveAll(id => !ids.Contains(id));
        if (preferences.DefaultGroupId is { } defaultId && !ids.Contains(defaultId)) { preferences.DefaultGroupId = null; }
        if (preferences.LastGroupId is { } lastId && !ids.Contains(lastId)) { preferences.LastGroupId = null; }
        return Ok(preferences);
    }

    /// <summary>Saves personal preferences without changing original Live TV.</summary>
    [HttpPut("Preferences")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult SetPreferences([FromBody, Required] GroupPreferences preferences)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        var ids = _groups.GetGroups(user).Select(g => g.Id).ToHashSet();
        if (preferences.HiddenGroupIds is null || preferences.HiddenGroupIds.Any(id => !ids.Contains(id))
            || (preferences.DefaultGroupId is { } selected && !ids.Contains(selected))
            || (preferences.LastGroupId is { } last && !ids.Contains(last))
            || (preferences.DefaultGroupId is { } defaultId && preferences.HiddenGroupIds.Contains(defaultId))
            || !GroupPreferences.IsValidView(preferences.DefaultView)
            || !GroupPreferences.IsValidView(preferences.LastView)
            || preferences.Zoom is not (3 or 5 or 8))
        {
            return BadRequest("Invalid group settings.");
        }
        preferences.HiddenGroupIds = preferences.HiddenGroupIds.Distinct().ToList();
        _groups.Store.Update(user.Id, doc =>
        {
            // Player preference has its own validated endpoint. Older clients and queued page writes
            // must not clear or override the current target.
            preferences.PreferredTargetDeviceId = doc.Preferences.PreferredTargetDeviceId;
            preferences.PreferredTargetDeviceName = doc.Preferences.PreferredTargetDeviceName;
            doc.Preferences = preferences;
            return true;
        });
        return NoContent();
    }

    /// <summary>Gets the independent groups container, respecting user access.</summary>
    [HttpGet("Entry")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public async Task<ActionResult> GetEntry()
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        var channels = await HttpContext.RequestServices.GetRequiredService<MediaBrowser.Controller.Channels.IChannelManager>()
            .GetChannelsAsync(new MediaBrowser.Model.Channels.ChannelQuery { UserId = user.Id }).ConfigureAwait(false);
        var channel = channels.Items.FirstOrDefault(c => c.Id == Channel.GroupsChannel.GetInternalId(HttpContext.RequestServices.GetRequiredService<ILibraryManager>()));
        var configuration = Plugin.Instance?.Configuration;
        var language = string.IsNullOrWhiteSpace(Request.Headers.AcceptLanguage.ToString())
            ? PluginLocalization.ServerLanguage(HttpContext.RequestServices)
            : Request.Headers.AcceptLanguage.ToString();
        return Ok(new
        {
            ChannelId = channel?.Id,
            HideOriginalLiveTvHomeEntry = configuration?.ShouldHideOriginalLiveTvHomeEntry(channel is not null) == true,
            JellyfinTvTargetsEnabled = configuration?.EnableJellyfinTvTargets != false,
            WholphinTargetsEnabled = configuration?.EnableWholphinTargets != false,
            DisplayName = PluginLocalization.DisplayName(configuration, language),
            Version = typeof(Plugin).Assembly.GetName().Version?.ToString()
        });
    }

    /// <summary>Gets available channels for the groups editor only.</summary>
    [HttpGet("AvailableChannels")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult GetAvailableChannels()
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        var channels = _groups.GetAccessibleChannels(user).Values.Cast<BaseItem>().ToList();
        var items = _dtoService.GetBaseItemDtos(channels, new DtoOptions(false), user);
        return Ok(new QueryResult<BaseItemDto>(0, items.Count, items));
    }

    /// <summary>Gets channels in the selected groups; this is not a native Live TV route.</summary>
    [HttpGet("Channels")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult GetScopeChannels([FromQuery] Guid[] groupIds)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        var scope = GetScope(user, groupIds);
        if (scope is null) { return NotFound(); }
        var channels = HttpContext.RequestServices.GetRequiredService<GroupGuideService>().ResolveScope(user, scope);
        var items = _dtoService.GetBaseItemDtos(channels.Cast<BaseItem>().ToList(),
            new DtoOptions(false) { AddCurrentProgram = true, EnableImages = true, ImageTypeLimit = 1, ImageTypes = [ImageType.Primary], EnableUserData = true }, user);
        return Ok(new QueryResult<BaseItemDto>(0, items.Count, items));
    }

    /// <summary>Gets the independent program guide for selected groups.</summary>
    [HttpGet("Guide")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public async Task<ActionResult<GuideDto>> GetScopeGuide([FromQuery] Guid[] groupIds, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        var scope = GetScope(user, groupIds);
        if (scope is null) { return NotFound(); }
        return Ok(await HttpContext.RequestServices.GetRequiredService<GroupGuideService>()
            .GetGuide(user, scope, start, end, HttpContext.RequestAborted).ConfigureAwait(false));
    }

    private List<ChannelGroup>? GetScope(User user, Guid[] ids)
    {
        var selected = ids.ToHashSet();
        var groups = _groups.GetGroups(user);
        return selected.Any(id => !groups.Any(g => g.Id == id)) ? null : groups.Where(g => selected.Contains(g.Id)).ToList();
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
    public ActionResult GetClientScript() => Content(LocalizationScript() + ReadScript("client.js"), "application/javascript");

    [HttpGet("localization.js")]
    [AllowAnonymous]
    public ActionResult GetLocalizationScript() => Content(LocalizationScript(), "application/javascript");

    private static string LocalizationScript()
        => "window.LiveTvGroupsTranslations=" + JsonSerializer.Serialize(PluginLocalization.Strings) + ";\n" + ReadScript("localization.js");

    private static string ReadScript(string file)
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.LiveTvGroups.Web." + file)!;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Serves the web client stylesheet.
    /// </summary>
    /// <returns>The stylesheet.</returns>
    [HttpGet("channel-access.js")]
    [AllowAnonymous]
    public ActionResult GetChannelAccessScript() => ServeResource("channel-access.js", "application/javascript");

    [HttpGet("client.css")]
    [AllowAnonymous]
    public ActionResult GetClientStyles() => ServeResource("client.css", "text/css");

    /// <summary>Entry fragment loaded by the optional Plugin Pages menu.</summary>
    [HttpGet("page.html")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult GetUserPage() => ServeResource("page.html", "text/html");

    /// <inheritdoc />
    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is UnauthorizedAccessException)
        {
            context.ExceptionHandled = true;
            context.Result = Forbid();
        }
        base.OnActionExecuted(context);
    }

    /// <summary>Gets editing mode without exposing other users permissions.</summary>
    [HttpGet("Access")]
    [Authorize(Policy = Policies.LiveTvAccess)]
    public ActionResult GetAccess()
    {
        var user = GetUser();
        return user is null ? Unauthorized() : Ok(new
        {
            Mode = _groups.Shared ? "shared" : "personal",
            CanManage = _groups.CanManage(user),
            IsAdministrator = user.HasPermission(PermissionKind.IsAdministrator)
        });
    }

    /// <summary>Gets central administration data and users.</summary>
    [HttpGet("Administration")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult GetAdministration()
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        if (!user.HasPermission(PermissionKind.IsAdministrator)) { return Forbid(); }
        return Ok(new
        {
            Configuration = _groups.Store.GetAdministration(),
            Users = _userManager.GetUsers().Select(u => new
            {
                Id = u.Id,
                Name = u.Username,
                IsAdministrator = u.HasPermission(PermissionKind.IsAdministrator),
                HasLiveTvAccess = !u.HasPermission(PermissionKind.IsDisabled) && u.HasPermission(PermissionKind.EnableLiveTvAccess)
            })
        });
    }

    /// <summary>Changes mode, optionally copying admin personal groups without deleting them.</summary>
    [HttpPut("Administration")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult> SetAdministration([FromBody, Required] AdministrationRequest request, CancellationToken cancellationToken = default)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        if (!user.HasPermission(PermissionKind.IsAdministrator)) { return Forbid(); }
        if (request.Mode is not ("personal" or "shared")) { return BadRequest("Invalid group mode."); }
        if (request.ImportPersonalGroups && request.Mode != "shared") { return BadRequest("Import is only available for central groups."); }
        var imported = new List<Guid>();
        _groups.Store.UpdateAdministration(config =>
        {
            config.Mode = request.Mode;
            if (request.ImportPersonalGroups)
            {
                foreach (var group in _groups.Store.Get(user.Id).Groups.Where(g => !config.Groups.Any(shared => shared.Id == g.Id)))
                {
                    var copy = JsonSerializer.Deserialize<ChannelGroup>(JsonSerializer.SerializeToUtf8Bytes(group))!;
                    copy.VisibleToAllUsers = true;
                    copy.AllowedUserIds.Clear();
                    copy.DeniedUserIds.Clear();
                    config.Groups.Add(copy);
                    imported.Add(copy.Id);
                }
            }
            return true;
        });
        if (imported.Count > 0) { Artwork.CopyPersonalToShared(user.Id, imported); }
        await RefreshVisibleArtworkAsync(cancellationToken).ConfigureAwait(false);
        QueueAllSync();
        return NoContent();
    }

    /// <summary>Sets the user access policy of a central group.</summary>
    [HttpPut("Administration/Groups/{groupId}/Access")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult SetGroupAccess(Guid groupId, [FromBody, Required] GroupAccessRequest request)
    {
        var user = GetUser();
        if (user is null) { return Unauthorized(); }
        if (!user.HasPermission(PermissionKind.IsAdministrator)) { return Forbid(); }
        var users = _userManager.GetUsers().Select(u => u.Id).ToHashSet();
        if (request.AllowedUserIds is null || request.DeniedUserIds is null
            || request.AllowedUserIds.Concat(request.DeniedUserIds).Any(id => !users.Contains(id)))
        { return BadRequest("Unknown user."); }
        var found = _groups.Store.UpdateAdministration(config =>
        {
            var group = config.Mode == "shared" ? config.Groups.FirstOrDefault(g => g.Id == groupId) : null;
            if (group is null) { return false; }
            group.VisibleToAllUsers = request.VisibleToAllUsers;
            group.AllowedUserIds = request.AllowedUserIds.Distinct().ToList();
            group.DeniedUserIds = request.DeniedUserIds.Distinct().ToList();
            return true;
        });
        if (!found) { return NotFound(); }
        QueueAllSync();
        return NoContent();
    }

    private void QueueAllSync()
    {
        foreach (var id in _userManager.GetUsers().Select(u => u.Id).Concat(_groups.Store.GetUserIds()).Distinct())
        { _playlistSync.QueueSync(id); }
    }

    private void QueueGroupSync(Guid userId)
    {
        if (_groups.Shared) { QueueAllSync(); }
        else { _playlistSync.QueueSync(userId); }
    }

    private GroupDto ToDto(ChannelGroup group, User user) => new(group.Id, group.Name,
        HttpContext.RequestServices.GetService<ChannelAccessService>()?.Configuration.Enabled == true
            ? _groups.ResolveChannels(user, group, _groups.GetAccessibleChannels(user)).Count : group.Channels.Count,
        ArtworkOrNull?.HasCustomImage(user.Id, _groups.Shared, group.Id) == true,
        group.ArtworkRevision);

    private GroupArtworkService Artwork => ArtworkOrNull ?? throw new InvalidOperationException("Artwork service is unavailable.");

    private GroupArtworkService? ArtworkOrNull => _artwork ?? HttpContext?.RequestServices.GetService<GroupArtworkService>();

    private async Task RefreshVisibleArtworkAsync(CancellationToken cancellationToken)
    {
        foreach (var candidate in _userManager.GetUsers())
        {
            foreach (var group in _groups.GetGroups(candidate))
            {
                var path = Artwork.GetGroupImage(candidate.Id, _groups.Shared, group.Id);
                await Artwork.UpdateCachedGroupImageAsync(group.Id, path, cancellationToken).ConfigureAwait(false);
            }
        }
    }

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
/// <param name="HasCustomImage">Whether the group uses uploaded artwork.</param>
/// <param name="ArtworkRevision">Revision used to invalidate image caches.</param>
public record GroupDto(Guid Id, string Name, int ChannelCount, bool HasCustomImage = false, int ArtworkRevision = 0);

/// <summary>
/// Program guide of a group.
/// </summary>
/// <param name="Start">Window start (UTC).</param>
/// <param name="End">Window end (UTC).</param>
/// <param name="Channels">Channels in group order.</param>
/// <param name="Programs">Programs in the window, ordered by start.</param>
/// <param name="MissingChannelCount">Unavailable stored channels.</param>
/// <param name="InvalidProgramCount">Rejected program records.</param>
public record GuideDto(DateTime Start, DateTime End, IReadOnlyList<BaseItemDto> Channels, IReadOnlyList<BaseItemDto> Programs, int MissingChannelCount = 0, int InvalidProgramCount = 0);

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

/// <summary>Admin-controlled operating mode.</summary>
public class AdministrationRequest
{
    /// <summary>Gets or sets personal or shared.</summary>
    public string Mode { get; set; } = "personal";
    /// <summary>Gets or sets whether to copy existing admin groups.</summary>
    public bool ImportPersonalGroups { get; set; }
}

/// <summary>User access policy for a central group.</summary>
public class GroupAccessRequest
{
    /// <summary>Gets or sets whether everyone is allowed unless denied.</summary>
    public bool VisibleToAllUsers { get; set; } = true;
    /// <summary>Gets or sets allowed users.</summary>
    public List<Guid> AllowedUserIds { get; set; } = [];
    /// <summary>Gets or sets denied users, taking precedence.</summary>
    public List<Guid> DeniedUserIds { get; set; } = [];
}
