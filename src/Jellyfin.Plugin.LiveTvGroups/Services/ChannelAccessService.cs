using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Runtime.CompilerServices;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.DependencyInjection;
namespace Jellyfin.Plugin.LiveTvGroups.Services;
/// <summary>One policy for group, native item and remote playback paths. Services stay lazy to avoid channel startup cycles.</summary>
public class ChannelAccessService(GroupStore store, IServiceProvider services)
{
    private readonly ConditionalWeakTable<object, ConditionalWeakTable<ChannelAccessConfiguration, Dictionary<Guid, HashSet<Guid>>>> _resolved = new();
    private static readonly ConditionalWeakTable<object, ChannelIndex> Indices = new();
    private sealed class ChannelIndex(IReadOnlyList<LiveTvChannel> channels)
    {
        public readonly int Count = channels.Count;
        public readonly Dictionary<Guid, LiveTvChannel> ById = channels.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        public readonly Dictionary<(string, string), List<LiveTvChannel>> BySource = channels.GroupBy(c => (c.ServiceName, c.ExternalId)).ToDictionary(g => g.Key, g => g.ToList());
        public readonly Dictionary<(string, string, string), List<LiveTvChannel>> ByLabel = channels.GroupBy(c => (c.ServiceName, Normalize(c.Number), Normalize(c.Name))).ToDictionary(g => g.Key, g => g.ToList());
    }
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    public const string TagPrefix = "ltvg-access-7b3792b4-";
    public const string OriginPrefix = "ltvg-origin-7b3792b4-";
    public ChannelAccessConfiguration Configuration => store.GetAdministration().ChannelAccess;
    public IReadOnlyList<LiveTvChannel> AllChannels() => services.GetRequiredService<ILiveTvManager>()
        .GetInternalChannels(new LiveTvChannelQuery(), new DtoOptions(false), CancellationToken.None).Items.OfType<LiveTvChannel>().ToList();
    public static string UserTag(Guid user) => TagPrefix + user.ToString("N");
    public static ChannelRef Reference(LiveTvChannel channel) => new()
    { ItemId = channel.Id, Name = channel.Name, Number = channel.Number, ServiceName = channel.ServiceName, ExternalId = channel.ExternalId };
    public static IReadOnlyList<LiveTvChannel> Resolve(ChannelRef reference, IReadOnlyList<LiveTvChannel> channels)
    {
        // Native query inventories are snapshots. Also invalidate when callers grow a collection.
        if (Indices.TryGetValue(channels, out var previous) && previous.Count != channels.Count) Indices.Remove(channels);
        var index = Indices.GetValue(channels, _ => new ChannelIndex(channels));
        // Source identity precedes an item ID; never assign an ACL by name alone.
        if (!string.IsNullOrEmpty(reference.ServiceName) && !string.IsNullOrEmpty(reference.ExternalId)
            && index.BySource.TryGetValue((reference.ServiceName, reference.ExternalId), out var exact)) return exact;
        if (index.ById.TryGetValue(reference.ItemId, out var original)
            && (string.IsNullOrEmpty(reference.ServiceName) || original.ServiceName == reference.ServiceName)
            && (string.IsNullOrEmpty(reference.ExternalId) || original.ExternalId == reference.ExternalId)) return [original];
        if (string.IsNullOrEmpty(reference.ServiceName) || string.IsNullOrEmpty(reference.Number)) return [];
        // A source + exact name/number fallback is conservative: all ambiguous matches stay subject to the rule.
        return index.ByLabel.GetValueOrDefault((reference.ServiceName, Normalize(reference.Number), Normalize(reference.Name))) ?? [];
    }
    public IReadOnlyList<string> Denials(User user, LiveTvChannel channel, ChannelAccessConfiguration? draft = null, IReadOnlyList<LiveTvChannel>? inventory = null)
    {
        var config = draft ?? Configuration;
        if (!config.Enabled || user.HasPermission(PermissionKind.IsAdministrator)) return [];
        inventory ??= AllChannels();
        var resolved = _resolved.GetValue(inventory, _ => new()).GetValue(config, cfg => cfg.Rules.ToDictionary(rule => rule.Id, rule => rule.Channels.SelectMany(r => Resolve(r, inventory)).Select(c => c.Id).ToHashSet()));
        return config.Rules.Where(rule => resolved[rule.Id].Contains(channel.Id)
            && (rule.DeniedUserIds.Contains(user.Id) || (!rule.VisibleToAllUsers && !rule.AllowedUserIds.Contains(user.Id))))
            .Select(rule => rule.Name).ToList();
    }
    public bool Allowed(User user, LiveTvChannel channel, IReadOnlyList<LiveTvChannel>? inventory = null)
        => !user.HasPermission(PermissionKind.IsDisabled) && user.HasPermission(PermissionKind.EnableLiveTvAccess)
            && Denials(user, channel, inventory: inventory).Count == 0;
    public IReadOnlyList<LiveTvChannel> ItemChannels(BaseItem item, IReadOnlyList<LiveTvChannel>? inventory = null, ChannelAccessConfiguration? draft = null)
    {
        inventory ??= AllChannels(); var config = draft ?? Configuration;
        if (item is LiveTvChannel live) return [live];
        if (GroupItemId.TryParse(item.ExternalId, out _, out var groupChannel))
            return inventory.Where(c => c.Id == groupChannel).ToList();
        if (!string.IsNullOrEmpty(item.ExternalId) && AppGuideService.TryParse(item.ExternalId, out var guide) && guide.Channel != Guid.Empty)
            return inventory.Where(c => c.Id == guide.Channel).ToList();
        if (item is LiveTvProgram || item.ChannelId != Guid.Empty && inventory.Any(c => c.Id == item.ChannelId))
            return inventory.Where(c => c.Id == item.ChannelId).ToList();
        var assignment = config.Recordings.FirstOrDefault(r => r.ItemId == item.Id
            || !string.IsNullOrEmpty(r.Path) && string.Equals(r.Path, item.Path, StringComparison.Ordinal));
        if (assignment is not null) return Resolve(assignment.Channel, inventory);
        // Jellyfin 12's built-in DVR exposes the original provider channel while a file is being recorded.
        // Its NFO writer does not copy program tags, so attribution must be captured explicitly.
        var active = string.IsNullOrEmpty(item.Path) ? null : services.GetService<IRecordingsManager>()?.GetActiveRecordingInfo(item.Path);
        if (active?.Timer?.ChannelId is { } external)
            return inventory.Where(c => c.ServiceName == "Emby" && c.ExternalId == external).ToList();
        var origins = item.Tags.Where(t => t.StartsWith(OriginPrefix, StringComparison.Ordinal));
        return origins.SelectMany(t => Guid.TryParseExact(t[OriginPrefix.Length..], "N", out var id)
            ? config.KnownChannels.Where(r => r.ItemId == id).SelectMany(r => Resolve(r, inventory)) : []).DistinctBy(c => c.Id).ToList();
    }
    public bool ItemAllowed(User user, BaseItem item, IReadOnlyList<LiveTvChannel>? inventory = null, ChannelAccessConfiguration? draft = null)
    {
        var config = draft ?? Configuration;
        if (!config.Enabled) return true;
        inventory ??= AllChannels();
        var channels = ItemChannels(item, inventory, config);
        // A missing assigned source must not unlock a previously protected recording or cached group item.
        var assigned = config.Recordings.FirstOrDefault(r => r.ItemId == item.Id || !string.IsNullOrEmpty(r.Path) && r.Path == item.Path);
        var originRefs = item.Tags.Where(t => t.StartsWith(OriginPrefix, StringComparison.Ordinal))
            .SelectMany(t => Guid.TryParseExact(t[OriginPrefix.Length..], "N", out var origin) ? config.KnownChannels.Where(r => r.ItemId == origin) : []).ToList();
        if (channels.Count == 0 && (assigned is not null || originRefs.Count > 0))
            return !user.HasPermission(PermissionKind.IsDisabled) && user.HasPermission(PermissionKind.EnableLiveTvAccess) && (user.HasPermission(PermissionKind.IsAdministrator) || !config.Rules.Any(rule => rule.Channels.Any(r => assigned is not null && SameReference(r, assigned.Channel) || originRefs.Any(origin => SameReference(r, origin)))
                && (rule.DeniedUserIds.Contains(user.Id) || !rule.VisibleToAllUsers && !rule.AllowedUserIds.Contains(user.Id))));
        if (channels.Count == 0 && (item is LiveTvProgram || GroupItemId.TryParse(item.ExternalId, out _, out _)
            || !string.IsNullOrEmpty(item.ExternalId) && AppGuideService.TryParse(item.ExternalId, out var guide) && guide.Channel != Guid.Empty)) return false;
        return channels.All(c => !user.HasPermission(PermissionKind.IsDisabled) && user.HasPermission(PermissionKind.EnableLiveTvAccess) && Denials(user, c, config, inventory).Count == 0);
    }
    public static bool SameReference(ChannelRef a, ChannelRef b)
        => !string.IsNullOrEmpty(a.ServiceName) && !string.IsNullOrEmpty(a.ExternalId) && !string.IsNullOrEmpty(b.ServiceName) && !string.IsNullOrEmpty(b.ExternalId)
            ? a.ServiceName == b.ServiceName && a.ExternalId == b.ExternalId : a.ItemId == b.ItemId;
    public IReadOnlyList<BaseItem> RecordingItems()
    {
        var library = services.GetRequiredService<ILibraryManager>();
        var manager = services.GetService<IRecordingsManager>();
        if (manager is null) return [];
        return manager.GetRecordingFolders().SelectMany(f => f.Locations)
            .Select(path => library.FindByPath(path, true)).Where(folder => folder is not null)
            .SelectMany(folder => library.GetItemList(new InternalItemsQuery { AncestorIds = [folder!.Id], Recursive = true }))
            .Where(i => !i.IsFolder).DistinctBy(i => i.Id).ToList();
    }
}
