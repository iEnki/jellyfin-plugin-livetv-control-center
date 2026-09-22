using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

/// <summary>Stores group artwork and exposes the built-in folder images.</summary>
public sealed class GroupArtworkService
{
    /// <summary>Maximum accepted upload size.</summary>
    public const long MaxImageBytes = 5 * 1024 * 1024;

    private const int MaxDimension = 16384;
    private const string ResourcePrefix = "Jellyfin.Plugin.LiveTvGroups.Artwork.";
    private static readonly string[] Extensions = ["png", "jpg", "webp"];
    private readonly Storage.GroupStore _store;
    private readonly IServiceProvider _services;
    private readonly ILogger<GroupArtworkService> _logger;
    private readonly object _defaultLock = new();

    /// <summary>Initializes a new instance.</summary>
    public GroupArtworkService(Storage.GroupStore store, IServiceProvider services, ILogger<GroupArtworkService> logger)
    {
        _store = store;
        _services = services;
        _logger = logger;
    }

    /// <summary>Gets the plugin logo used by the channel root.</summary>
    public string RootLogoPath => EnsureDefault("root-logo.png", "RootLogo.png");

    /// <summary>Gets the standard native-guide image.</summary>
    public string EpgPath => EnsureDefault("epg.png", "Epg.png");

    /// <summary>Gets the image for restoring all TV guide channels.</summary>
    public string AllChannelsPath => EnsureDefault("all-channels.png", "AllChannels.png");

    /// <summary>Gets the standard image for a created channel group.</summary>
    public string GroupPath => EnsureDefault("group.png", "Group.png");

    /// <summary>Gets the fallback program-list image.</summary>
    public string ProgramListPath => EnsureDefault("program-list.png", "ProgramList.png");

    /// <summary>Gets a group's custom image or the standard group image.</summary>
    public string GetGroupImage(Guid userId, bool shared, Guid groupId)
        => FindCustom(userId, shared, groupId) ?? GroupPath;

    /// <summary>Gets whether a group has custom artwork.</summary>
    public bool HasCustomImage(Guid userId, bool shared, Guid groupId)
        => FindCustom(userId, shared, groupId) is not null;

    /// <summary>Validates and atomically stores an uploaded group image.</summary>
    public async Task<string> SaveCustomImageAsync(
        Guid userId,
        bool shared,
        Guid groupId,
        Stream input,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (mediaType is not ("image/png" or "image/jpeg" or "image/webp"))
        {
            throw new InvalidDataException("Only PNG, JPEG and WebP images are supported.");
        }

        var directory = GetScopeDirectory(userId, shared);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + groupId.ToString("N") + "." + Guid.NewGuid().ToString("N") + ".upload");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxImageBytes)
                    {
                        throw new InvalidDataException("The image must not exceed 5 MiB.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                if (total == 0)
                {
                    throw new InvalidDataException("The image is empty.");
                }
            }

            var extension = DetectExtension(temporary);
            var expected = mediaType switch { "image/png" => "png", "image/jpeg" => "jpg", _ => "webp" };
            if (!string.Equals(extension, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The image content does not match its file type.");
            }

            try
            {
                var size = _services.GetRequiredService<IImageEncoder>().GetImageSize(temporary);
                if (size.Width <= 0 || size.Height <= 0 || size.Width > MaxDimension || size.Height > MaxDimension)
                {
                    throw new InvalidDataException("The image dimensions are invalid or too large.");
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new InvalidDataException("Jellyfin could not decode this image.", error);
            }

            var destination = Path.Combine(directory, groupId.ToString("N") + "." + extension);
            File.Move(temporary, destination, true);
            foreach (var other in Extensions.Select(ext => Path.Combine(directory, groupId.ToString("N") + "." + ext)).Where(path => path != destination && File.Exists(path)))
            {
                File.Delete(other);
            }

            File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Deletes a group's custom artwork.</summary>
    public bool DeleteCustomImage(Guid userId, bool shared, Guid groupId)
    {
        var removed = false;
        foreach (var path in CandidatePaths(userId, shared, groupId).Where(File.Exists))
        {
            File.Delete(path);
            removed = true;
        }

        return removed;
    }

    /// <summary>Copies personal artwork when the administrator imports groups into central mode.</summary>
    public void CopyPersonalToShared(Guid userId, IEnumerable<Guid> groupIds)
    {
        foreach (var groupId in groupIds)
        {
            var source = FindCustom(userId, false, groupId);
            if (source is null)
            {
                continue;
            }

            var targetDirectory = GetScopeDirectory(userId, true);
            Directory.CreateDirectory(targetDirectory);
            var target = Path.Combine(targetDirectory, groupId.ToString("N") + Path.GetExtension(source).ToLowerInvariant());
            File.Copy(source, target, true);
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        }
    }

    /// <summary>Updates an already cached Jellyfin group folder so clients receive a new image tag.</summary>
    public Task UpdateCachedGroupImageAsync(Guid groupId, string imagePath, CancellationToken cancellationToken)
        => UpdateCachedFolderImageAsync(GroupsChannel.GetFolderExternalId(groupId), imagePath, cancellationToken);

    /// <summary>Updates a cached provider folder image when that item already exists.</summary>
    internal async Task UpdateCachedFolderImageAsync(string externalId, string imagePath, CancellationToken cancellationToken)
    {
        var library = _services.GetService<ILibraryManager>();
        var fileSystem = _services.GetService<IFileSystem>();
        if (library is null || fileSystem is null)
        {
            return;
        }

        try
        {
            var itemId = library.GetNewItemId(externalId + GroupsChannel.ChannelName + "16", typeof(Folder));
            if (library.GetItemById(itemId) is not Folder item)
            {
                return;
            }

            item.SetImagePath(ImageType.Primary, 0, fileSystem.GetFileInfo(imagePath));
            item.DateModified = DateTime.UtcNow;
            item.OnMetadataChanged();
            await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning(error, "Could not refresh cached artwork for folder {ExternalId}", externalId);
        }
    }

    /// <summary>Gets the MIME type for a stored image.</summary>
    public static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "image/png"
    };

    private string EnsureDefault(string fileName, string resourceName)
    {
        var directory = Path.Combine(GetArtworkRoot(), "defaults");
        var destination = Path.Combine(directory, fileName);
        lock (_defaultLock)
        {
            using var resource = typeof(GroupArtworkService).Assembly.GetManifestResourceStream(ResourcePrefix + resourceName)
                ?? throw new InvalidOperationException("Missing embedded artwork: " + resourceName);
            if (!File.Exists(destination) || new FileInfo(destination).Length != resource.Length)
            {
                Directory.CreateDirectory(directory);
                var temporary = destination + ".tmp";
                using (var output = File.Create(temporary))
                {
                    resource.CopyTo(output);
                }

                File.Move(temporary, destination, true);
            }
        }

        return destination;
    }

    private string GetArtworkRoot()
        => Path.Combine(Directory.GetParent(_store.StorageDirectory)?.FullName ?? _store.StorageDirectory, "artwork");

    private string GetScopeDirectory(Guid userId, bool shared)
        => shared
            ? Path.Combine(GetArtworkRoot(), "shared")
            : Path.Combine(GetArtworkRoot(), "users", userId.ToString("N"));

    private IEnumerable<string> CandidatePaths(Guid userId, bool shared, Guid groupId)
        => Extensions.Select(ext => Path.Combine(GetScopeDirectory(userId, shared), groupId.ToString("N") + "." + ext));

    private string? FindCustom(Guid userId, bool shared, Guid groupId)
        => CandidatePaths(userId, shared, groupId).FirstOrDefault(File.Exists);

    private static string DetectExtension(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) < header.Length)
        {
            throw new InvalidDataException("The image file is incomplete.");
        }

        if (header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return "jpg";
        if (header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return "webp";
        throw new InvalidDataException("Only PNG, JPEG and WebP images are supported.");
    }
}
