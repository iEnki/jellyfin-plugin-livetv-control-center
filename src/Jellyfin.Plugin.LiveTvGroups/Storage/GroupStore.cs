using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private GroupAdministration? _administration;

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
    /// Gets the ids of all users that have stored groups.
    /// </summary>
    /// <returns>User ids.</returns>
    public IReadOnlyList<Guid> GetUserIds()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => Guid.TryParse(name, out var id) ? id : Guid.Empty)
            .Where(id => !id.Equals(Guid.Empty))
            .ToList();
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

    /// <summary>Gets a read-only snapshot of the central configuration.</summary>
    public GroupAdministration GetAdministration()
    {
        lock (_writeLock)
        {
            if (_administration is null)
            {
                var file = Path.Combine(_directory, "administration.json");
                _administration = File.Exists(file)
                    ? JsonSerializer.Deserialize<GroupAdministration>(File.ReadAllBytes(file)) ?? new() : new();
            }
            return _administration;
        }
    }

    public bool TryUpdateAdministration(int expectedRevision, Action<GroupAdministration> change)
    {
        lock (_writeLock)
        {
            if (GetAdministration().Revision != expectedRevision) return false;
            UpdateAdministration(config => { change(config); return true; });
            return true;
        }
    }

    /// <summary>Atomically updates central configuration.</summary>
    public T UpdateAdministration<T>(Func<GroupAdministration, T> change)
    {
        lock (_writeLock)
        {
            var copy = JsonSerializer.Deserialize<GroupAdministration>(JsonSerializer.SerializeToUtf8Bytes(GetAdministration()))!;
            var result = change(copy);
            copy.Revision++;
            Directory.CreateDirectory(_directory);
            var file = Path.Combine(_directory, "administration.json");
            File.WriteAllBytes(file + ".tmp", JsonSerializer.SerializeToUtf8Bytes(copy, JsonOptions));
            File.Move(file + ".tmp", file, overwrite: true);
            _administration = copy;
            return result;
        }
    }

    /// <summary>Updates the active collection under the same lock as mode changes.</summary>
    public T UpdateGroups<T>(Guid userId, bool administrator, Func<UserGroups, T> change)
    {
        lock (_writeLock)
        {
            if (GetAdministration().Mode != "shared") { return Update(userId, change); }
            if (!administrator) { throw new UnauthorizedAccessException("Zentrale Gruppen dürfen nur Administratoren bearbeiten."); }
            return UpdateAdministration(config =>
            {
                var doc = new UserGroups { Groups = config.Groups };
                var result = change(doc);
                config.Groups = doc.Groups;
                return result;
            });
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
