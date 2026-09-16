using System.Text.RegularExpressions;
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

    [Fact]
    public void LeavesContentWithoutBodyUnchanged()
    {
        const string chunk = "(self.webpackChunk=self.webpackChunk||[]).push([[17244],{1:e=>{e.exports='<div> </div>'}}]);";

        Assert.Equal(chunk, IndexTransformer.Transform(new TransformationPayload { Contents = chunk }));
    }

    [Theory]
    [InlineData("index.html", true)]
    [InlineData("session-login-index-html.748f2ea433c30fd200a8.chunk.js", false)]
    [InlineData("main.jellyfin.bundle.js", false)]
    [InlineData("index.htmlx", false)]
    [InlineData("foo/index.html.map", false)]
    [InlineData("xindex.html", false)]
    public void PatternOnlyMatchesIndexHtml(string path, bool expected)
        => Assert.Equal(expected, Regex.IsMatch(path, IndexTransformer.FileNamePattern));
}
