using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Data;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Channel;

/// <summary>Only handles this plugin's channel videos. Native Live TV items and other plugin channels remain untouched.</summary>
public class GroupsMediaSourceProvider(GroupService groups, IServiceProvider services) : IMediaSourceProvider
{
    private Guid CurrentUserId => Guid.TryParse(services.GetService<IHttpContextAccessor>()?.HttpContext?.User
        .FindFirstValue("Jellyfin-UserId"), out var id) ? id : Guid.Empty;

    private LiveTvChannel? Resolve(string? externalId, Guid userId)
    {
        if (userId == Guid.Empty || !GroupItemId.TryParse(externalId, out var groupId, out var channelId)) return null;
        var user = services.GetRequiredService<IUserManager>().GetUserById(userId);
        if (user is null || !user.HasPermission(PermissionKind.EnableLiveTvAccess)) return null;
        var scope = groups.GetGroups(user).Where(g => groupId is null || g.Id == groupId).ToList();
        var available = groups.GetAccessibleChannels(user);
        return scope.SelectMany(g => groups.ResolveChannels(user, g, available)).FirstOrDefault(c => c.Id == channelId);
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken cancellationToken)
    {
        if (item is not Video || item.ChannelId == Guid.Empty
            || services.GetRequiredService<ILibraryManager>().GetItemById(item.ChannelId)?.Name != GroupsChannel.ChannelName)
            return [];
        var userId = CurrentUserId;
        var channel = Resolve(item.ExternalId, userId);
        if (channel is null)
        {
            services.GetService<ILogger<GroupsMediaSourceProvider>>()?.LogWarning("Unable to resolve an accessible grouped TV channel for the current request (authenticated user: {Authenticated})", userId != Guid.Empty);
            return [];
        }
        var result = await services.GetRequiredService<LiveTvStreamBridge>().GetMediaSources(channel, cancellationToken).ConfigureAwait(false);
        return result.Select(source =>
        {
            // Never mutate a source returned by another provider; its original source id is needed to open the tuner.
            var copy = JsonSerializer.Deserialize<MediaSourceInfo>(JsonSerializer.Serialize(source))!;
            var nativeToken = string.IsNullOrEmpty(copy.OpenToken)
                ? "LiveTvChannel_" + channel.Id.ToString("N", CultureInfo.InvariantCulture) + "_" + (copy.Id ?? string.Empty)
                : copy.OpenToken;
            copy.Id ??= Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.ExternalId + "_" + nativeToken))).Substring(0, 32).ToLowerInvariant();
            copy.OpenToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new OpenRequest(userId, item.ExternalId!, nativeToken))));
            copy.RequiresOpening = true;
            copy.RequiresClosing = true;
            copy.IsInfiniteStream = true;
            copy.RunTimeTicks = null;
            copy.Type = MediaSourceType.Default;
            copy.LiveStreamId = null;
            return copy;
        }).ToList();
    }

    public Task<ILiveStream> OpenMediaSource(string openToken, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        OpenRequest request;
        try
        {
            if (openToken.Length > 8192) throw new FormatException();
            request = JsonSerializer.Deserialize<OpenRequest>(Convert.FromBase64String(openToken)) ?? throw new FormatException();
            if (string.IsNullOrEmpty(request.ExternalId) || string.IsNullOrEmpty(request.NativeToken)) throw new FormatException();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new ArgumentException("Ungültige Gruppen-Medienquelle.", nameof(openToken), ex);
        }
        if (CurrentUserId != request.UserId) throw new UnauthorizedAccessException("Diese Gruppen-Medienquelle gehört einem anderen Benutzer.");
        var channel = Resolve(request.ExternalId, request.UserId) ?? throw new UnauthorizedAccessException("Sender nicht mehr verfügbar oder nicht erlaubt.");
        var prefix = "LiveTvChannel_" + channel.Id.ToString("N", CultureInfo.InvariantCulture) + "_";
        if (!request.NativeToken.StartsWith(prefix, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Die Medienquelle gehört nicht zu diesem Sender.");
        // Return the real ILiveStream unchanged, including direct-stream interfaces and Close()/sharing/connection-limit behavior.
        // Calling IMediaSourceManager.OpenLiveStream here would recursively acquire its stream lock and deadlock.
        return services.GetRequiredService<LiveTvStreamBridge>().OpenMediaSource(request.NativeToken, currentLiveStreams, cancellationToken);
    }

    private sealed record OpenRequest(Guid UserId, string ExternalId, string NativeToken);
}

/// <summary>Validated external ids, including cached videos and playlist references from earlier versions.</summary>
internal static class GroupItemId
{
    public static bool TryParse(string? value, out Guid? group, out Guid channel)
    {
        group = null; channel = Guid.Empty;
        if (string.IsNullOrEmpty(value)) return false;
        var parts = value.Split('_');
        if (parts.Length == 3 && parts[0] is "ltvch4" or "ltvch3" or "ltvch2"
            && Guid.TryParseExact(parts[1], "N", out var id) && Guid.TryParseExact(parts[2], "N", out channel))
        { group = id; return true; }
        if (parts.Length is 4 or 5 && parts[0] == "ltvepglive" && Guid.TryParseExact(parts[1], "N", out var epgGroup)
            && Guid.TryParseExact(parts[2], "N", out _) && Guid.TryParseExact(parts[^1], "N", out channel)
            && (parts.Length == 4 || (parts[3].Length == 12 && parts[3].All(Uri.IsHexDigit))))
        { group = epgGroup; return true; }
        return parts.Length == 2 && parts[0] is "ltvchannel" or "ltvch2" or "ltvch3"
            && Guid.TryParseExact(parts[1], "N", out channel);
    }
}
