using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.LiveTvGroups.Model;

namespace Jellyfin.Plugin.LiveTvGroups.Storage;

/// <summary>
/// Persists groups as one JSON file per user.
/// </summary>
public class GroupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly ConcurrentDictionary<Guid, UserGroups> _cache = new();
    private readonly Lock _writeLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupStore"/> class.
    /// </summary>
    /// <param name="directory">Directory for the user files.</param>
    public GroupStore(string directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// Gets a snapshot of the groups of a user. Callers must not modify it.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The groups.</returns>
    public UserGroups Get(Guid userId) => _cache.GetOrAdd(userId, Load);

    /// <summary>
    /// Applies a change to a copy of the user's groups and persists it.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="userId">The user id.</param>
    /// <param name="change">Change to apply; return value is passed through.</param>
    /// <returns>The value returned by <paramref name="change"/>.</returns>
    public T Update<T>(Guid userId, Func<UserGroups, T> change)
    {
        lock (_writeLock)
        {
            var copy = Clone(Get(userId));
            var result = change(copy);
            copy.Revision++;
            Save(userId, copy);
            _cache[userId] = copy;
            return result;
        }
    }

    /// <summary>
    /// Removes all data of a user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    public void Delete(Guid userId)
    {
        lock (_writeLock)
        {
            _cache.TryRemove(userId, out _);
            var path = GetPath(userId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static UserGroups Clone(UserGroups source)
        => JsonSerializer.Deserialize<UserGroups>(JsonSerializer.SerializeToUtf8Bytes(source))!;

    private string GetPath(Guid userId) => Path.Combine(_directory, userId.ToString("N") + ".json");

    private UserGroups Load(Guid userId)
    {
        var path = GetPath(userId);
        if (!File.Exists(path))
        {
            return new UserGroups();
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<UserGroups>(stream) ?? new UserGroups();
    }

    private void Save(Guid userId, UserGroups groups)
    {
        Directory.CreateDirectory(_directory);
        var path = GetPath(userId);
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(groups, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
