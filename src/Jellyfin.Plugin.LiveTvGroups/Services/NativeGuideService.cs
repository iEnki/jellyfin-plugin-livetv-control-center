using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Storage;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Explicit device scopes; rights and source mappings are resolved afresh for every request.</summary>
public class NativeGuideService(GroupStore store, GroupService groups)
{
    /// <summary>Existing single-group lookup, retained for native folder actions.</summary>
    public Guid? Get(Guid userId, string deviceId) => GetSelection(userId, deviceId)?.GroupId;

    public NativeGuideSelection? GetSelection(Guid userId, string deviceId) => Selection(store.Get(userId), deviceId);

    private static NativeGuideSelection? Selection(UserGroups doc, string deviceId)
        => doc.NativeGuideVisibleGroupDevices.Contains(deviceId) ? new(null, true)
            : doc.NativeGuideScopes.TryGetValue(deviceId, out var group) ? new(group) : null;

    public void Set(User user, string deviceId, Guid groupId)
    {
        if (Resolve(user, groupId) is not { Count: > 0 }) throw new NativeGuideGroupUnavailableException();
        store.Update(user.Id, doc =>
        {
            doc.NativeGuideVisibleGroupDevices.Remove(deviceId);
            doc.NativeGuideScopes[deviceId] = groupId;
            return true;
        });
    }

    public void SetVisibleGroups(User user, string deviceId)
    {
        if (Resolve(user, new NativeGuideSelection(null, true)) is not { Count: > 0 }) throw new NativeGuideGroupUnavailableException();
        store.Update(user.Id, doc =>
        {
            doc.NativeGuideScopes.Remove(deviceId);
            doc.NativeGuideVisibleGroupDevices.Add(deviceId);
            return true;
        });
    }

    public void Clear(Guid userId, string deviceId, Guid? expected = null)
        => ClearCore(userId, deviceId, expected is null ? null : new NativeGuideSelection(expected));

    public void ClearSelection(Guid userId, string deviceId, NativeGuideSelection expected)
        => ClearCore(userId, deviceId, expected);

    private void ClearCore(Guid userId, string deviceId, NativeGuideSelection? expected)
    {
        if (GetSelection(userId, deviceId) is null) return;
        store.Update(userId, doc =>
        {
            // Do not erase a concurrently replaced single-group/union selection during stale-scope cleanup.
            if (expected is null || Selection(doc, deviceId) == expected)
            {
                doc.NativeGuideScopes.Remove(deviceId);
                doc.NativeGuideVisibleGroupDevices.Remove(deviceId);
            }
            return true;
        });
    }

    /// <summary>Null denotes an invalid selection; it must not hide ordinary Live TV.</summary>
    public IReadOnlySet<Guid>? Resolve(User user, NativeGuideSelection selection)
    {
        if (!selection.AllVisibleGroups) return selection.GroupId is Guid group ? Resolve(user, group) : null;
        if (selection.GroupId is not null) return null;
        var hidden = store.Get(user.Id).Preferences.HiddenGroupIds.ToHashSet();
        var visible = groups.GetGroups(user).Where(g => !hidden.Contains(g.Id)).ToArray();
        if (visible.Length == 0) return null;
        var accessible = groups.GetAccessibleChannels(user);
        var resolved = visible.Select(g => (g.Id, Channels: groups.ResolveChannels(user, g, accessible))).ToArray();
        // Visibility and group access can change during source resolution. Never retain a removed/hidden group.
        hidden = store.Get(user.Id).Preferences.HiddenGroupIds.ToHashSet();
        var stillVisible = groups.GetGroups(user).Where(g => !hidden.Contains(g.Id)).Select(g => g.Id).ToHashSet();
        var channels = resolved.Where(g => stillVisible.Contains(g.Id)).SelectMany(g => g.Channels).Select(c => c.Id).ToHashSet();
        return channels.Count > 0 ? channels : null;
    }

    public IReadOnlySet<Guid>? Resolve(User user, Guid groupId)
    {
        var group = groups.GetGroups(user).FirstOrDefault(g => g.Id == groupId);
        if (group is null) return null;
        var channels = groups.ResolveChannels(user, group, groups.GetAccessibleChannels(user));
        // ResolveChannels re-reads policy; check again to distinguish revocation from an empty group.
        if (!groups.GetGroups(user).Any(g => g.Id == groupId) || channels.Count == 0) return null;
        return channels.Select(c => c.Id).ToHashSet();
    }
}

/// <summary>A persisted selection is either one group or the dynamic visible-group union, never a reset.</summary>
public sealed record NativeGuideSelection(Guid? GroupId, bool AllVisibleGroups = false);

/// <summary>A selection cannot be activated without currently accessible group channels.</summary>
public sealed class NativeGuideGroupUnavailableException : Exception
{
    public NativeGuideGroupUnavailableException() : base("Selection unavailable or has no accessible group channels.") { }
}
