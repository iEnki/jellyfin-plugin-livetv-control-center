using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LiveTvGroups.Storage;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Explicit device scopes; rights and source mappings are resolved afresh for every request.</summary>
public class NativeGuideService(GroupStore store, GroupService groups)
{
    public Guid? Get(Guid userId, string deviceId)
        => store.Get(userId).NativeGuideScopes.TryGetValue(deviceId, out var group) ? group : null;

    public void Set(User user, string deviceId, Guid groupId)
    {
        if (Resolve(user, groupId) is not { Count: > 0 })
            throw new NativeGuideGroupUnavailableException();
        store.Update(user.Id, doc => { doc.NativeGuideScopes[deviceId] = groupId; return true; });
    }

    public void Clear(Guid userId, string deviceId, Guid? expected = null)
    {
        if (Get(userId, deviceId) is null) return;
        store.Update(userId, doc =>
        {
            if (expected is null || doc.NativeGuideScopes.GetValueOrDefault(deviceId) == expected)
                doc.NativeGuideScopes.Remove(deviceId);
            return true;
        });
    }

    /// <summary>Null denotes an invalid group; it must not hide ordinary Live TV.</summary>
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

/// <summary>A selection cannot be activated without a currently accessible group.</summary>
public sealed class NativeGuideGroupUnavailableException : Exception
{
    public NativeGuideGroupUnavailableException() : base("Group unavailable or has no accessible channels.") { }
}
