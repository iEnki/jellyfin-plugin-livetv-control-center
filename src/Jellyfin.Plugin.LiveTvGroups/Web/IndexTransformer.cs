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
    /// <summary>
    /// File Transformation runs only the pipeline of an exact key match ("index.html", used by other plugins) and
    /// ignores other patterns for that file, so the same key must be used. As a regex it also matches files like
    /// "session-login-index-html.*.chunk.js"; <see cref="Transform"/> therefore only changes real HTML documents.
    /// </summary>
    public const string FileNamePattern = "index.html";

    // index.html is served from /web/, so a relative path works with any configured base URL.
    private const string ScriptTag = "<script src=\"../LiveTvGroups/client.js?v=0.3.0.1\" defer></script>";

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

        // Only touch real HTML documents; JavaScript chunks and other files are returned unchanged.
        var head = contents.TrimStart();
        if (!head.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            && !head.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return contents;
        }

        var index = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? contents : contents.Insert(index, ScriptTag);
    }
}
