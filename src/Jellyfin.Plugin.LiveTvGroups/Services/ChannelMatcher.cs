using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LiveTvGroups.Model;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>
/// Minimal view of an available channel, independent of Jellyfin entities.
/// </summary>
/// <param name="Id">Item id.</param>
/// <param name="Name">Channel name.</param>
/// <param name="Number">Channel number.</param>
public record AvailableChannel(Guid Id, string Name, string? Number);

/// <summary>
/// Matches stored channel references against the currently available channels.
/// </summary>
public static class ChannelMatcher
{
    /// <summary>
    /// Resolves references. Unknown ids are re-matched by name and number, then by name alone
    /// (only if the name is unique); references without a match are skipped.
    /// </summary>
    /// <param name="refs">Stored references in display order.</param>
    /// <param name="available">Channels the user may access.</param>
    /// <returns>Matched channels in display order, and whether any stored reference must be updated.</returns>
    public static (IReadOnlyList<AvailableChannel> Channels, bool RefsChanged) Match(
        IEnumerable<ChannelRef> refs,
        IReadOnlyCollection<AvailableChannel> available)
    {
        var byId = available.ToDictionary(c => c.Id);
        var byName = available
            .GroupBy(c => Normalize(c.Name))
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<AvailableChannel>();
        var seen = new HashSet<Guid>();
        var changed = false;

        foreach (var reference in refs)
        {
            if (!byId.TryGetValue(reference.ItemId, out var channel)
                && byName.TryGetValue(Normalize(reference.Name), out var candidates))
            {
                channel = candidates.FirstOrDefault(c => string.Equals(c.Number, reference.Number, StringComparison.OrdinalIgnoreCase))
                    ?? (candidates.Count == 1 ? candidates[0] : null);
                changed |= channel is not null;
            }

            if (channel is not null && seen.Add(channel.Id))
            {
                result.Add(channel);
            }
        }

        return (result, changed);
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
