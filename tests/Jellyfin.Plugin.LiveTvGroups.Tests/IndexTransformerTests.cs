using Jellyfin.Plugin.LiveTvGroups.Web;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class IndexTransformerTests
{
    private static string Transform(string contents) => IndexTransformer.Transform(new TransformationPayload { Contents = contents });

    [Theory]
    [InlineData("<!DOCTYPE html><html><body><div></div></body></html>")]
    [InlineData("\n  <html lang=\"de\"><body></body></html>")]
    public void InsertsScriptOnceBeforeBody(string html)
    {
        var once = Transform(html);
        var twice = Transform(once);

        Assert.Equal(once, twice);
        Assert.EndsWith("defer></script></body></html>", once, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(self.webpackChunk=self.webpackChunk||[]).push([[17244],{1:e=>{e.exports='<div> </div>'}}]);")]
    [InlineData("(self.webpackChunk=self.webpackChunk||[]).push([[1],{1:e=>{e.exports='<html><body></body></html>'}}]);")]
    [InlineData("body { color: red; }")]
    public void LeavesNonHtmlDocumentsUnchanged(string contents)
        => Assert.Equal(contents, Transform(contents));

    [Fact]
    public void UsesSharedIndexHtmlKey()
        => Assert.Equal("index.html", IndexTransformer.FileNamePattern);
}
