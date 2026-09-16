using System;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Web;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class GuideWindowTests
{
    private static readonly DateTime Now = new(2026, 9, 16, 18, 47, 12, DateTimeKind.Utc);

    [Fact]
    public void DefaultsToNowRoundedDownToHalfHour()
    {
        var (start, end) = GuideWindow.Normalize(null, null, Now);

        Assert.Equal(new DateTime(2026, 9, 16, 18, 30, 0, DateTimeKind.Utc), start);
        Assert.Equal(start + GuideWindow.DefaultLength, end);
    }

    [Fact]
    public void LimitsWindowTo24Hours()
    {
        var (start, end) = GuideWindow.Normalize(Now, Now.AddDays(3), Now);

        Assert.Equal(TimeSpan.FromHours(24), end - start);
    }

    [Fact]
    public void ReplacesInvertedWindow()
    {
        var (start, end) = GuideWindow.Normalize(Now, Now.AddHours(-2), Now);

        Assert.Equal(Now, start);
        Assert.Equal(Now + GuideWindow.DefaultLength, end);
    }
}

public class GuideFilterTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static string Channels(params Guid[] ids)
        => new JsonObject
        {
            ["Items"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject { ["Id"] = id.ToString("N"), ["Name"] = id.ToString() }).ToArray()),
            ["TotalRecordCount"] = ids.Length,
            ["StartIndex"] = 0
        }.ToJsonString();

    private static Guid[] Ids(string json)
        => JsonNode.Parse(json)!["Items"]!.AsArray().Select(i => Guid.Parse(i!["Id"]!.GetValue<string>())).ToArray();

    [Fact]
    public void KeepsOnlyGroupChannelsInGroupOrder()
    {
        var result = GuideFilter.FilterChannels(Channels(A, B, C), [C, A], null, null)!;

        Assert.Equal([C, A], Ids(result));
        Assert.Equal(2, JsonNode.Parse(result)!["TotalRecordCount"]!.GetValue<int>());
    }

    [Fact]
    public void AppliesPagingAfterFiltering()
    {
        var result = GuideFilter.FilterChannels(Channels(A, B, C), [A, B, C], 1, 1)!;

        Assert.Equal([B], Ids(result));
        Assert.Equal(3, JsonNode.Parse(result)!["TotalRecordCount"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Name\":\"x\"}")]
    [InlineData("[]")]
    public void LeavesOtherDocumentsUnchanged(string json)
        => Assert.Null(GuideFilter.FilterChannels(json, [A], null, null));

    [Theory]
    [InlineData("Jellyfin Web", "Jellyfin Web", true)]
    [InlineData("jellyfin web", "Jellyfin Web, Kodi", true)]
    [InlineData("Android TV", "Jellyfin Web", false)]
    [InlineData(null, "Jellyfin Web", false)]
    [InlineData("Android TV", "", false)]
    public void DetectsExcludedClients(string? client, string excluded, bool expected)
        => Assert.Equal(expected, GuideFilter.IsClientExcluded(client, excluded));
}
