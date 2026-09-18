using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Jellyfin.Plugin.LiveTvGroups.Api;

[ApiController]
[Route("LiveTvGroups/Administration/ChannelAccess")]
[Authorize(Policy = Policies.RequiresElevation)]
public class ChannelAccessController(GroupStore store, ChannelAccessService access, ChannelAccessTagBridge bridge,
    ChannelAccessRevoker revoker, IUserManager users, ILibraryManager library, PlaylistSyncService playlists) : ControllerBase
{
    private User? Actor() => Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var id) ? users.GetUserById(id) : null;
    private bool Administrator(User? user) => user is not null && !user.HasPermission(PermissionKind.IsDisabled) && user.HasPermission(PermissionKind.IsAdministrator);
    [HttpGet]
    public ActionResult Get()
    {
        var actor = Actor(); if (actor is null) return Unauthorized(); if (!Administrator(actor)) return Forbid();
        var inventory = access.AllChannels(); var snapshot = store.GetAdministration(); var config = snapshot.ChannelAccess;
        return Ok(new
        {
            Revision = snapshot.Revision,
            config.Enabled,
            config.Rules,
            Assignments = config.Recordings,
            Users = users.GetUsers().Select(u => new { u.Id, Name = u.Username, IsAdministrator = u.HasPermission(PermissionKind.IsAdministrator), HasLiveTvAccess = !u.HasPermission(PermissionKind.IsDisabled) && u.HasPermission(PermissionKind.EnableLiveTvAccess) }),
            Channels = inventory.Select(ChannelAccessService.Reference),
            References = config.Rules.SelectMany(rule => rule.Channels.Select((reference, index) => new
            {
                RuleId = rule.Id,
                Index = index,
                reference.Name,
                reference.Number,
                Matches = ChannelAccessService.Resolve(reference, inventory).Select(c => c.Id),
                Status = ChannelAccessService.Resolve(reference, inventory) is { Count: 0 } ? "missing"
                    : ChannelAccessService.Resolve(reference, inventory).Count > 1 ? "ambiguous" : "resolved"
            })),
            Recordings = access.RecordingItems().Select(i => new
            {
                i.Id,
                i.Name,
                Channel = config.Recordings.FirstOrDefault(r => r.ItemId == i.Id || !string.IsNullOrEmpty(r.Path) && r.Path == i.Path)?.Channel,
                Channels = access.ItemChannels(i, inventory).Select(c => c.Id)
            })
        });
    }
    private string? Validate(ChannelAccessRequest request)
    {
        if (request.Rules is null || request.Recordings is null || request.Rules.Any(r => r is null) || request.Recordings.Any(r => r is null || r.Channel is null) || request.Rules.Count > 200 || request.Recordings.Count > 10000) return "Invalid policy document.";
        if (request.Rules.Select(r => r.Id).Distinct().Count() != request.Rules.Count) return "Duplicate rule ID.";
        var knownUsers = users.GetUsers().Select(u => u.Id).ToHashSet(); var inventory = access.AllChannels();
        foreach (var rule in request.Rules)
        {
            if (rule.Id == Guid.Empty || string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 80 || rule.Channels is null || rule.AllowedUserIds is null || rule.DeniedUserIds is null || rule.Channels.Any(r => r is null || r.Name is null) || rule.Channels.Count > 10000) return "Invalid rule.";
            var original = access.Configuration.Rules.FirstOrDefault(r => r.Id == rule.Id);
            if (rule.AllowedUserIds.Concat(rule.DeniedUserIds).Any(id => !knownUsers.Contains(id) && !(original?.AllowedUserIds.Contains(id) == true || original?.DeniedUserIds.Contains(id) == true))) return "Unknown user.";
            foreach (var reference in rule.Channels)
            {
                if (original?.Channels.Any(r => r.ItemId == reference.ItemId && r.ServiceName == reference.ServiceName && r.ExternalId == reference.ExternalId && r.Name == reference.Name && r.Number == reference.Number) == true) continue;
                var current = inventory.FirstOrDefault(c => c.Id == reference.ItemId);
                if (current is not null)
                { if (!ChannelAccessService.SameReference(reference, ChannelAccessService.Reference(current)) || reference.ServiceName != current.ServiceName || reference.ExternalId != current.ExternalId) return "Channel source identity changed. Reload the inventory."; }
                else if (original is null || !original.Channels.Any(r => r.ItemId == reference.ItemId && r.ServiceName == reference.ServiceName && r.ExternalId == reference.ExternalId && r.Name == reference.Name && r.Number == reference.Number)) return "Unknown channel reference.";
            }
        }
        var recordings = access.RecordingItems();
        if (request.Recordings.Select(r => r.ItemId).Distinct().Count() != request.Recordings.Count) return "Duplicate recording assignment.";
        foreach (var assignment in request.Recordings)
        {
            var item = recordings.FirstOrDefault(i => i.Id == assignment.ItemId);
            if (item is null && !access.Configuration.Recordings.Any(r => r.ItemId == assignment.ItemId && r.Path == assignment.Path && ChannelAccessService.SameReference(r.Channel, assignment.Channel))) return "Unknown recording.";
            if (item is not null) assignment.Path = item.Path; // Never trust a client-supplied filesystem path.
            if (!inventory.Any(c => c.Id == assignment.Channel.ItemId && assignment.Channel.ServiceName == c.ServiceName && assignment.Channel.ExternalId == c.ExternalId)
                && !access.Configuration.Recordings.Any(r => r.ItemId == assignment.ItemId && ChannelAccessService.SameReference(r.Channel, assignment.Channel))) return "Unknown recording channel.";
        }
        if (request.PreviewUserId is { } user && !knownUsers.Contains(user)) return "Unknown preview user.";
        return null;
    }
    [HttpPut]
    public async Task<ActionResult> Put([FromBody] ChannelAccessRequest request)
    {
        var actor = Actor(); if (actor is null) return Unauthorized(); if (!Administrator(actor)) return Forbid();
        var error = Validate(request); if (error is not null) return BadRequest(error);
        var applied = store.TryUpdateAdministration(request.Revision, doc =>
        {
            var trusted = JsonSerializer.Deserialize<ChannelAccessRequest>(JsonSerializer.Serialize(request))!;
            doc.ChannelAccess.PolicyRevision++; doc.ChannelAccess.Enabled = trusted.Enabled; doc.ChannelAccess.Rules = trusted.Rules; doc.ChannelAccess.Recordings = trusted.Recordings;
        });
        if (!applied) return Conflict("Channel access changed. Reload before saving.");
        bridge.Invalidate();
        // A disconnected admin client must not cancel enforcement of an already persisted change.
        await revoker.RevokeAsync(CancellationToken.None).ConfigureAwait(false);
        await bridge.EnsureAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var user in users.GetUsers()) playlists.QueueSync(user.Id);
        return Get();
    }
    public static User BaseRightsUser(User source)
    {
        var clone = new User(source.Username, "preview", "preview");
        foreach (var property in typeof(User).GetProperties().Where(p => p.CanRead && p.SetMethod?.IsPublic == true && (p.PropertyType.IsValueType || p.PropertyType == typeof(string))))
            property.SetValue(clone, property.GetValue(source));
        foreach (var kind in Enum.GetValues<PermissionKind>()) clone.SetPermission(kind, source.HasPermission(kind));
        foreach (var kind in Enum.GetValues<PreferenceKind>()) clone.SetPreference(kind,
            source.GetPreference(kind).Where(t => kind != PreferenceKind.BlockedTags || !t.StartsWith(ChannelAccessService.TagPrefix, StringComparison.Ordinal)).ToArray());
        return clone;
    }
    [HttpPost("Preview")]
    public ActionResult Preview([FromBody] ChannelAccessRequest request)
    {
        var actor = Actor(); if (actor is null) return Unauthorized(); if (!Administrator(actor)) return Forbid();
        var error = Validate(request); if (error is not null) return BadRequest(error);
        var draft = new ChannelAccessConfiguration { Enabled = request.Enabled, Rules = request.Rules, Recordings = request.Recordings, KnownChannels = access.Configuration.KnownChannels };
        var inventory = access.AllChannels();
        var selected = request.PreviewUserId is { } preview ? users.GetUserById(preview)! : actor;
        var baseChannels = library.GetItemList(new InternalItemsQuery(BaseRightsUser(selected)) { IncludeItemTypes = [BaseItemKind.LiveTvChannel], Recursive = true }).Select(i => i.Id).ToHashSet();
        return Ok(new
        {
            Channels = inventory.Select(c => new
            {
                c.Id,
                c.Name,
                c.Number,
                c.ServiceName,
                Allowed = !selected.HasPermission(PermissionKind.IsDisabled) && selected.HasPermission(PermissionKind.EnableLiveTvAccess) && baseChannels.Contains(c.Id) && access.Denials(selected, c, draft, inventory).Count == 0,
                Rules = access.Denials(selected, c, draft, inventory),
                JellyfinAllowed = baseChannels.Contains(c.Id)
            }),
            Users = users.GetUsers().Select(u => new { u.Id, Name = u.Username, DeniedCount = inventory.Count(c => access.Denials(u, c, draft, inventory).Count > 0) }),
            ActivePlayback = HttpContext.RequestServices.GetService(typeof(MediaBrowser.Controller.Session.ISessionManager)) is MediaBrowser.Controller.Session.ISessionManager sessions
                ? sessions.Sessions.Where(s => s.NowPlayingItem is not null).Select(s => new
                {
                    s.UserId,
                    s.DeviceName,
                    Name = s.NowPlayingItem!.Name,
                    WillStop = users.GetUserById(s.UserId) is { } owner && library.GetItemById(s.NowPlayingItem.Id) is { } item && !access.ItemAllowed(owner, item, inventory, draft)
                }).ToArray() : []
        });
    }
}
