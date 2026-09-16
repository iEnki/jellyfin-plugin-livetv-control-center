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

        var index = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? contents + ScriptTag : contents.Insert(index, ScriptTag);
    }
}
