using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Jellyfin.Plugin.LiveTvGroups.Model;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Read-only guide data for the independent groups page.</summary>
public class GroupGuideService(GroupService groups, IServiceProvider services)
{
    public IReadOnlyList<LiveTvChannel> ResolveScope(User user, IReadOnlyList<ChannelGroup> scope)
    {
        return ResolveScope(user, scope, out _);
    }

    private IReadOnlyList<LiveTvChannel> ResolveScope(User user, IReadOnlyList<ChannelGroup> scope, out int missing)
    {
        var available = groups.GetAccessibleChannels(user);
        var channels = new List<LiveTvChannel>();
        missing = 0;
        foreach (var group in scope)
        {
            var resolved = groups.ResolveChannels(user, group, available);
            missing += Math.Max(0, group.Channels.Count - resolved.Count);
            channels.AddRange(resolved);
        }
        return channels.DistinctBy(c => c.Id).ToList();
    }

    public async Task<GuideDto> GetGuide(User user, IReadOnlyList<ChannelGroup> scope, DateTime? start, DateTime? end, CancellationToken cancellationToken)
    {
        var (from, to) = GuideWindow.Normalize(start, end, DateTime.UtcNow);
        var channels = ResolveScope(user, scope, out var missing);
        var channelDtos = services.GetRequiredService<IDtoService>().GetBaseItemDtos(channels.Cast<BaseItem>().ToList(),
            new DtoOptions(false) { EnableImages = true, ImageTypeLimit = 1, ImageTypes = [ImageType.Primary] }, user);
        IReadOnlyList<BaseItemDto> programs = [];
        var invalid = 0;
        if (channels.Count > 0)
        {
            var result = await services.GetRequiredService<ILiveTvManager>().GetPrograms(
                new InternalItemsQuery(user)
                {
                    ChannelIds = channels.Select(c => c.Id).ToArray(), MinEndDate = from, MaxStartDate = to,
                    OrderBy = [(ItemSortBy.StartDate, SortOrder.Ascending)]
                }, new DtoOptions(false) { EnableImages = true, ImageTypeLimit = 1, ImageTypes = [ImageType.Primary], EnableUserData = true }, cancellationToken).ConfigureAwait(false);
            programs = ValidPrograms(result.Items, channels.Select(c => c.Id).ToHashSet(), from, to);
            invalid = result.Items.Count - programs.Count;
        }
        return new GuideDto(from, to, channelDtos, programs, missing, invalid);
    }

    internal static IReadOnlyList<BaseItemDto> ValidPrograms(IReadOnlyList<BaseItemDto> programs, IReadOnlySet<Guid> channelIds, DateTime start, DateTime end)
        => programs.Where(p => p.ChannelId is { } id && channelIds.Contains(id)
            && p.StartDate is { } from && p.EndDate is { } to && to > from && to > start && from < end)
            .DistinctBy(p => p.Id).OrderBy(p => p.StartDate).ToList();
}
