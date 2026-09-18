using System;
using System.Globalization;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Explicit native folder actions, separate from group browsing and program-list IDs.</summary>
public static class NativeGuideActionRoute
{
    private const string Prefix = "ltvnativeguide_";
    public const string AllChannelsId = Prefix + "all";
    public static string GroupId(Guid groupId) => Prefix + groupId.ToString("N", CultureInfo.InvariantCulture);
    public static bool TryParse(string? externalId, out Guid? groupId)
    {
        groupId = null;
        if (externalId == AllChannelsId) return true;
        if (externalId?.StartsWith(Prefix, StringComparison.Ordinal) != true
            || !Guid.TryParseExact(externalId[Prefix.Length..], "N", out var parsed) || parsed == Guid.Empty) return false;
        groupId = parsed;
        return true;
    }
    public static string HelpId(Guid? groupId) => "ltvnativeguidehelp_" + (groupId?.ToString("N", CultureInfo.InvariantCulture) ?? "all");
}
