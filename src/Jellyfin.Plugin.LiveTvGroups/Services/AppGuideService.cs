using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LiveTvGroups.Model;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Browsable program information for native apps that cannot load the independent web guide.</summary>
public class AppGuideService(GroupService groups, IServiceProvider services)
{
    private const string Root = "ltvepg", Day = "ltvepgday", Channel = "ltvepgchannel", Program = "ltvepgprogram";
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-AT");

    public static string GetRootId(Guid groupId) => Root + "_" + groupId.ToString("N");
    public static bool Handles(string folderId) => folderId.StartsWith("ltvepg", StringComparison.Ordinal);

    public async Task<ChannelItemResult> GetItems(User user, string folderId, CancellationToken cancellationToken)
    {
        if (!TryParse(folderId, out var route)) return Empty();
        var group = groups.Store.Get(user.Id).Groups.FirstOrDefault(g => g.Id == route.Group);
        if (group is null) return Empty();
        var zone = GetTimeZone(Plugin.Instance?.Configuration.AppGuideTimeZone);
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        if (route.Kind == Root)
        {
            var days = Enumerable.Range(0, 7).Select(offset =>
            {
                var date = today.AddDays(offset);
                var title = offset == 0 ? "Heute" : offset == 1 ? "Morgen" : date.ToString("dddd", German);
                return Folder(Day + "_" + route.Group.ToString("N") + "_" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    title + " · " + date.ToString("dd.MM.yyyy", German), offset + 1, "Fernsehprogramm der Gruppe „" + group.Name + "“. Zeitangaben gemäß Plugin-Einstellungen.");
            }).ToList();
            return Result(days);
        }
        if (route.Date < today || route.Date > today.AddDays(6)) return Empty();
        var (start, end) = DayWindow(route.Date, zone);
        var guideService = services.GetRequiredService<GroupGuideService>();
        var guide = await guideService.GetGuide(user, [group], start, end, cancellationToken).ConfigureAwait(false);
        var programs = guide.Programs;
        // GuideWindow intentionally limits individual requests to 24h. A DST fall-back day can have 25h.
        if (end - start > TimeSpan.FromHours(24))
        {
            var tail = await guideService.GetGuide(user, [group], start.AddHours(24), end, cancellationToken).ConfigureAwait(false);
            programs = programs.Concat(tail.Programs).DistinctBy(p => p.Id).OrderBy(p => p.StartDate).ToList();
        }
        var channels = groups.ResolveChannels(user, group, groups.GetAccessibleChannels(user));
        if (route.Kind == Day)
        {
            return Result(channels.Select((channel, index) =>
            {
                var count = programs.Count(p => p.ChannelId == channel.Id);
                var overview = "Fernsehprogramm dieses Senders für den gewählten Tag. Falls die Liste leer ist, wurden für diesen Tag keine Programmdaten importiert.";
                var item = Folder(Channel + "_" + route.Group.ToString("N") + "_" + route.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + channel.Id.ToString("N"),
                    channel.Name + (count == 0 ? " · Keine Programmdaten" : string.Empty), index + 1, overview);
                item.ImageUrl = LocalLogo(channel);
                return item;
            }).ToList());
        }
        var selectedChannel = channels.FirstOrDefault(c => c.Id == route.Channel);
        if (selectedChannel is null) return Empty();
        if (route.Kind == Channel)
        {
            var items = programs.Where(p => p.ChannelId == selectedChannel.Id).Select((program, index) =>
            {
                var item = Folder(Program + "_" + route.Group.ToString("N") + "_" + route.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + selectedChannel.Id.ToString("N") + "_" + program.Id.ToString("N") + "_" + Fingerprint(program, zone),
                    Range(program, zone) + " · " + program.Name, index + 1, Overview(program, selectedChannel.Name, zone));
                item.StartDate = program.StartDate; item.EndDate = program.EndDate;
                item.ImageUrl = LocalLogo(selectedChannel);
                return item;
            }).ToList();
            return Result(items);
        }
        var selectedProgram = programs.FirstOrDefault(p => p.Id == route.Program && p.ChannelId == selectedChannel.Id);
        if (selectedProgram is null) return Empty();
        // A future or past program never pretends to be a recording. The child explicitly opens the LIVE channel.
        return Result([new ChannelItemInfo
        {
            Id = "ltvepglive_" + group.Id.ToString("N") + "_" + selectedProgram.Id.ToString("N") + "_" + Fingerprint(selectedProgram, zone) + "_" + selectedChannel.Id.ToString("N"),
            Name = selectedChannel.Name + " live ansehen", Overview = Overview(selectedProgram, selectedChannel.Name, zone)
                + "\n\nDieser Eintrag startet den aktuellen Live-Sender. Er ist keine Aufnahme oder Wiederholung dieser Sendung.",
            Type = ChannelItemType.Media, MediaType = ChannelMediaType.Video, ContentType = ChannelMediaContentType.Clip,
            IsLiveStream = true, IndexNumber = 1, ImageUrl = LocalLogo(selectedChannel)
        }]);
    }

    internal static TimeZoneInfo GetTimeZone(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id ?? "Europe/Vienna"); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    internal static (DateTime Start, DateTime End) DayWindow(DateTime day, TimeZoneInfo zone)
        => (TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified), zone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.Date.AddDays(1), DateTimeKind.Unspecified), zone));

    // Jellyfin copies channel Overview only at item creation. Changed program metadata needs a new item id.
    private static string Fingerprint(BaseItemDto program, TimeZoneInfo zone)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { program.Name, program.EpisodeTitle, program.Overview, program.StartDate, program.EndDate, Zone = zone.Id })))).Substring(0, 12);

    private static string Range(BaseItemDto program, TimeZoneInfo zone)
        => Local(program.StartDate!.Value, zone).ToString("dd.MM. HH:mm", German) + " – " + Local(program.EndDate!.Value, zone).ToString("HH:mm", German);
    private static DateTime Local(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
    private static string Overview(BaseItemDto p, string channel, TimeZoneInfo zone)
        => channel + " · " + Range(p, zone) + " (" + zone.Id + ")\n" + p.Name + (string.IsNullOrEmpty(p.EpisodeTitle) ? string.Empty : "\n" + p.EpisodeTitle)
            + (string.IsNullOrEmpty(p.Overview) ? string.Empty : "\n\n" + p.Overview);
    private static string? LocalLogo(LiveTvChannel channel)
    {
        var image = channel.GetImageInfo(ImageType.Primary, 0);
        return image?.IsLocalFile == true ? image.Path : null;
    }
    private static ChannelItemInfo Folder(string id, string name, int index, string overview)
        => new() { Id = id, Name = name, Overview = overview, IndexNumber = index, Type = ChannelItemType.Folder, FolderType = ChannelFolderType.Container };
    private static ChannelItemResult Result(List<ChannelItemInfo> items) => new() { Items = items, TotalRecordCount = items.Count };
    private static ChannelItemResult Empty() => Result([]);

    internal sealed record Route(string Kind, Guid Group, DateTime Date, Guid Channel, Guid Program);
    internal static bool TryParse(string value, out Route route)
    {
        route = new Route(string.Empty, Guid.Empty, default, Guid.Empty, Guid.Empty);
        var parts = value.Split('_');
        if (parts.Length < 2 || !Guid.TryParseExact(parts[1], "N", out var group)) return false;
        if (parts[0] == Root && parts.Length == 2) { route = new Route(Root, group, default, Guid.Empty, Guid.Empty); return true; }
        var length = parts[0] switch { Day => 3, Channel => 4, Program => parts.Length == 6 ? 6 : 5, _ => 0 };
        if (length == 0 || parts.Length != length || !DateTime.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
        var channel = Guid.Empty; var program = Guid.Empty;
        if (length >= 4 && !Guid.TryParseExact(parts[3], "N", out channel)) return false;
        if (length >= 5 && !Guid.TryParseExact(parts[4], "N", out program)) return false;
        if (length == 6 && (parts[5].Length != 12 || !parts[5].All(Uri.IsHexDigit))) return false;
        route = new Route(parts[0], group, date, channel, program); return true;
    }
}
