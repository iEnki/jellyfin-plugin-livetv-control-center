using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Jellyfin.Plugin.LiveTvGroups.Services;
/// <summary>Projects the authoritative policy into native queries, preserving unrelated tags and user preferences.</summary>
public class ChannelAccessTagBridge(GroupStore store, ChannelAccessService access, IServiceProvider services, ILogger<ChannelAccessTagBridge> logger) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, string[]> _ownUpdates = [];
    private long _dirty = 1, _synchronized;
    private int _revision = -1;
    private string _users = string.Empty;
    private HashSet<Guid> _protected = [];
    private static string Fingerprint(IUserManager users) => string.Join(";", users.GetUsers().OrderBy(u => u.Id).Select(u => $"{u.Id}:{u.HasPermission(PermissionKind.IsAdministrator)}:{u.HasPermission(PermissionKind.IsDisabled)}:{u.HasPermission(PermissionKind.EnableLiveTvAccess)}"));
    public void Invalidate() => Interlocked.Increment(ref _dirty);
    public async Task EnsureAsync(CancellationToken token)
    {
        if (!access.Configuration.Enabled && access.Configuration.KnownChannels.Count == 0 && access.Configuration.Rules.Count == 0 && access.Configuration.Recordings.Count == 0) return;
        var users = services.GetRequiredService<IUserManager>();
        // User changes can occur outside library events: detect missing or unexpected owned restrictions.
        if (users.GetUsers().Any(u => (access.Configuration.Enabled && !u.HasPermission(PermissionKind.IsAdministrator))
            != u.GetPreference(PreferenceKind.BlockedTags).Contains(ChannelAccessService.UserTag(u.Id)))) Invalidate();
        var fingerprint = Fingerprint(users);
        if (fingerprint != _users) Invalidate();
        if (_synchronized == Interlocked.Read(ref _dirty) && _revision == store.GetAdministration().Revision) return;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            while (true)
            {
                fingerprint = Fingerprint(users);
                var epoch = Interlocked.Read(ref _dirty);
                if (_synchronized == epoch && _revision == store.GetAdministration().Revision) return;
                var library = services.GetRequiredService<ILibraryManager>();
                var inventory = access.AllChannels();
                var config = access.Configuration;
                var unknown = inventory.Where(c => !config.KnownChannels.Any(r => r.ItemId == c.Id)).ToList();
                if (config.Enabled && unknown.Count > 0)
                    store.UpdateAdministration(doc => { doc.ChannelAccess.KnownChannels.AddRange(unknown.Select(ChannelAccessService.Reference)); return true; });
                config = access.Configuration;
                var programs = library.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.LiveTvProgram], Recursive = true });
                var recordings = access.RecordingItems();
                var groupItems = library.GetItemList(new InternalItemsQuery { ChannelIds = [GroupsChannel.GetInternalId(library)], Recursive = true });
                var assignments = recordings.Where(i => !config.Recordings.Any(r => r.ItemId == i.Id || !string.IsNullOrEmpty(r.Path) && r.Path == i.Path))
                    .Select(i => (Item: i, Channels: access.ItemChannels(i, inventory))).Where(v => v.Channels.Count == 1).ToList();
                if (assignments.Count > 0)
                    store.UpdateAdministration(doc =>
                    {
                        doc.ChannelAccess.Recordings.AddRange(assignments.Where(v => !doc.ChannelAccess.Recordings.Any(r => r.ItemId == v.Item.Id || !string.IsNullOrEmpty(r.Path) && r.Path == v.Item.Path)).Select(v => new RecordingChannelAssignment
                        { ItemId = v.Item.Id, Path = v.Item.Path, Channel = ChannelAccessService.Reference(v.Channels[0]) })); return true;
                    });
                var snapshot = store.GetAdministration();
                config = snapshot.ChannelAccess;
                var projectedRevision = snapshot.Revision;
                _protected = (config.Enabled ? config.Rules : []).SelectMany(r => r.Channels.SelectMany(c => ChannelAccessService.Resolve(c, inventory))).Select(c => c.Id).ToHashSet();
                var allUsers = users.GetUsers().ToList();
                foreach (var item in inventory.Cast<BaseItem>().Concat(programs).Concat(recordings).Concat(groupItems).DistinctBy(i => i.Id))
                {
                    token.ThrowIfCancellationRequested();
                    var tags = item.Tags.Where(t => !t.StartsWith(ChannelAccessService.TagPrefix, StringComparison.Ordinal)
                        && !t.StartsWith(ChannelAccessService.OriginPrefix, StringComparison.Ordinal)).ToList();
                    if (config.Enabled)
                    {
                        var sources = access.ItemChannels(item, inventory, config);
                        if (sources.Count == 0) tags.AddRange(item.Tags.Where(t => t.StartsWith(ChannelAccessService.OriginPrefix, StringComparison.Ordinal)));
                        if (item is not LiveTvChannel and not LiveTvProgram && !GroupItemId.TryParse(item.ExternalId, out _, out _))
                            tags.AddRange(sources.Select(c => ChannelAccessService.OriginPrefix + c.Id.ToString("N")));
                        // Core already checks Live TV permissions for channels/programs; avoid tagging the whole EPG for disabled users.
                        tags.AddRange(allUsers.Where(u => item is LiveTvChannel or LiveTvProgram || GroupItemId.TryParse(item.ExternalId, out _, out _)
                            ? sources.Any(c => access.Denials(u, c, config, inventory).Count > 0)
                            : !access.ItemAllowed(u, item, inventory, config)).Select(u => ChannelAccessService.UserTag(u.Id)));
                    }
                    var updated = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (item.Tags.SequenceEqual(updated)) continue;
                    var originalTags = item.Tags;
                    lock (_ownUpdates) _ownUpdates[item.Id] = updated;
                    try
                    {
                        item.Tags = updated;
                        await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, token).ConfigureAwait(false);
                    }
                    catch { item.Tags = originalTags; throw; }
                    finally { lock (_ownUpdates) _ownUpdates.Remove(item.Id); }
                }
                foreach (var user in allUsers)
                {
                    var original = user.GetPreference(PreferenceKind.BlockedTags);
                    var updated = original.Where(t => !t.StartsWith(ChannelAccessService.TagPrefix, StringComparison.Ordinal)).ToList();
                    if (config.Enabled && !user.HasPermission(PermissionKind.IsAdministrator)) updated.Add(ChannelAccessService.UserTag(user.Id));
                    if (original.SequenceEqual(updated)) continue;
                    user.SetPreference(PreferenceKind.BlockedTags, updated.ToArray());
                    try { await users.UpdateUserAsync(user).ConfigureAwait(false); }
                    catch { user.SetPreference(PreferenceKind.BlockedTags, original); throw; }
                }
                _revision = projectedRevision;
                _synchronized = epoch;
                _users = fingerprint;
                if (Fingerprint(users) != fingerprint) Invalidate();
                if (_revision == store.GetAdministration().Revision && _synchronized == Interlocked.Read(ref _dirty)) break;
            }
        }
        finally { _gate.Release(); }
    }
    private void Changed(object? sender, ItemChangeEventArgs args)
    {
        lock (_ownUpdates) { if (_ownUpdates.TryGetValue(args.Item.Id, out var expected) && args.Item.Tags.SequenceEqual(expected)) return; }
        if (args.Item is LiveTvChannel || args.Item is LiveTvProgram && (_protected.Contains(args.Item.ChannelId) || args.Item.Tags.Any(t => t.StartsWith(ChannelAccessService.TagPrefix, StringComparison.Ordinal))) || GroupItemId.TryParse(args.Item.ExternalId, out _, out _)
            || args.Item.Tags.Any(t => t.StartsWith(ChannelAccessService.OriginPrefix, StringComparison.Ordinal))
            || access.Configuration.Recordings.Any(r => r.ItemId == args.Item.Id || !string.IsNullOrEmpty(r.Path) && r.Path == args.Item.Path)
            || services.GetService<MediaBrowser.Controller.LiveTv.IRecordingsManager>()?.GetRecordingFolders().SelectMany(f => f.Locations).Any(path => !string.IsNullOrEmpty(args.Item.Path) && args.Item.Path.StartsWith(path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) == true)
            Invalidate();
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var library = services.GetRequiredService<ILibraryManager>();
        library.ItemAdded += Changed; library.ItemUpdated += Changed; library.ItemRemoved += Changed;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            do
            {
                try { await EnsureAsync(stoppingToken).ConfigureAwait(false); if (access.Configuration.Enabled && services.GetService<ChannelAccessRevoker>() is { } revoker) await revoker.RevokeAsync(stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error) { logger.LogError(error, "Native channel access synchronization failed; protected API queries will remain unavailable until synchronization succeeds."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        finally { library.ItemAdded -= Changed; library.ItemUpdated -= Changed; library.ItemRemoved -= Changed; }
    }
}
