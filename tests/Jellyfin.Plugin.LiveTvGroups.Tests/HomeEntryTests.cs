using Jellyfin.Plugin.LiveTvGroups.Configuration;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class HomeEntryTests
{
    [Fact]
    public void ExistingInstallationsKeepOriginalHomeEntryByDefault()
        => Assert.False(new PluginConfiguration().HideOriginalLiveTvHomeEntry);

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, false, false, false, false)]
    public void EffectiveOptionRequiresEnabledIntegrationsAndAuthorizedChannel(
        bool hide, bool web, bool app, bool accessible, bool expected)
    {
        var config = new PluginConfiguration
        {
            HideOriginalLiveTvHomeEntry = hide,
            EnableWebIntegration = web,
            EnableAppChannel = app
        };
        Assert.Equal(expected, config.ShouldHideOriginalLiveTvHomeEntry(accessible));
    }
}