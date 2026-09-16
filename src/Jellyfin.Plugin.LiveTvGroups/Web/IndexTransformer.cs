using System;

namespace Jellyfin.Plugin.LiveTvGroups.Web;

/// <summary>
/// Payload passed by the File Transformation plugin.
/// </summary>
public class TransformationPayload
{
    /// <summary>
    /// Gets or sets the current file contents.
    /// </summary>
    public string Contents { get; set; } = string.Empty;
}

/// <summary>
/// Adds the client script to index.html. Invoked by File Transformation via reflection.
/// </summary>
public static class IndexTransformer
{
    // index.html is served from /web/, so a relative path works with any configured base URL.
    /// <summary>
    /// File Transformation matches this regex against the path relative to /web/. It must be anchored and escaped:
    /// "index.html" also matched chunks like "session-login-index-html.*.chunk.js" and broke the web client.
    /// </summary>
    public const string FileNamePattern = @"^index\.html$";

    private const string ScriptTag = "<script src=\"../LiveTvGroups/client.js\" defer></script>";

    /// <summary>
    /// Transforms index.html.
    /// </summary>
    /// <param name="payload">The file contents.</param>
    /// <returns>The transformed contents.</returns>
    public static string Transform(TransformationPayload payload)
    {
        var contents = payload.Contents;
        if (contents.Contains(ScriptTag, StringComparison.Ordinal))
        {
            return contents;
        }

        // Only touch real HTML documents; anything else is returned unchanged.
        var index = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? contents : contents.Insert(index, ScriptTag);
    }
}
