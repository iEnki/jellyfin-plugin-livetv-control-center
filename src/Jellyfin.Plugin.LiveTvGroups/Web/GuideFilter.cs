using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.LiveTvGroups.Web;

/// <summary>
/// Filters the channel list returned to TV apps down to the channels of a group.
/// </summary>
public static class GuideFilter
{
    /// <summary>
    /// Checks whether a client always receives all channels.
    /// </summary>
    /// <param name="client">Client name from the authorization header (e.g. "Android TV").</param>
    /// <param name="excludedClients">Comma separated client names.</param>
    /// <returns><c>true</c> if the client is excluded.</returns>
    public static bool IsClientExcluded(string? client, string? excludedClients)
    {
        if (string.IsNullOrWhiteSpace(client) || string.IsNullOrWhiteSpace(excludedClients))
        {
            return false;
        }

        return excludedClients
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(name => string.Equals(name, client.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters a <c>QueryResult&lt;BaseItemDto&gt;</c> JSON document to the given channels, in the given order,
    /// and applies paging afterwards.
    /// </summary>
    /// <param name="json">Response body of <c>GET /LiveTv/Channels</c>.</param>
    /// <param name="channelIds">Channel ids in display order.</param>
    /// <param name="startIndex">Original start index.</param>
    /// <param name="limit">Original limit.</param>
    /// <returns>The filtered JSON, or <c>null</c> if the document is not a channel result.</returns>
    public static string? FilterChannels(string json, IReadOnlyList<Guid> channelIds, int? startIndex, int? limit)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (root?["Items"] is not JsonArray items)
        {
            return null;
        }

        var position = channelIds
            .Select((id, index) => (id, index))
            .GroupBy(x => x.id)
            .ToDictionary(g => g.Key, g => g.First().index);

        var filtered = items
            .OfType<JsonObject>()
            .Select(item => (item, index: Guid.TryParse(item["Id"]?.GetValue<string>(), out var id) && position.TryGetValue(id, out var i) ? i : -1))
            .Where(x => x.index >= 0)
            .OrderBy(x => x.index)
            .Select(x => x.item)
            .ToList();

        var skip = Math.Max(0, startIndex ?? 0);
        var page = filtered.Skip(skip);
        if (limit is > 0)
        {
            page = page.Take(limit.Value);
        }

        var result = new JsonArray();
        foreach (var item in page.ToList())
        {
            items.Remove(item);
            result.Add(item);
        }

        root["Items"] = result;
        root["TotalRecordCount"] = filtered.Count;
        root["StartIndex"] = skip;
        return root.ToJsonString();
    }
}
