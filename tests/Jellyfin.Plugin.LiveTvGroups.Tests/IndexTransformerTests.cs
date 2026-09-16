using Jellyfin.Plugin.LiveTvGroups.Web;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class IndexTransformerTests
{
    [Fact]
    public void InsertsScriptOnceBeforeBody()
    {
        var once = IndexTransformer.Transform(new TransformationPayload { Contents = "<html><body><div></div></body></html>" });
        var twice = IndexTransformer.Transform(new TransformationPayload { Contents = once });

        Assert.Equal(once, twice);
        Assert.EndsWith("defer></script></body></html>", once, System.StringComparison.Ordinal);
    }
}
