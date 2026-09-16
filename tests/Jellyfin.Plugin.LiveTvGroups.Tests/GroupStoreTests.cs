using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public sealed class GroupStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ltvg-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void PersistsPerUserAndIncrementsRevision()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var store = new GroupStore(_directory);

        store.Update(alice, doc => { doc.Groups.Add(new ChannelGroup { Id = Guid.NewGuid(), Name = "Sport" }); return true; });

        var reloaded = new GroupStore(_directory);
        Assert.Equal("Sport", Assert.Single(reloaded.Get(alice).Groups).Name);
        Assert.Equal(1, reloaded.Get(alice).Revision);
        Assert.Empty(reloaded.Get(bob).Groups);
    }

    [Fact]
    public void ThrowingChangeDoesNotModifySnapshot()
    {
        var user = Guid.NewGuid();
        var store = new GroupStore(_directory);

        Assert.Throws<InvalidOperationException>(() => store.Update<bool>(user, doc =>
        {
            doc.Groups.Add(new ChannelGroup { Name = "Kaputt" });
            throw new InvalidOperationException();
        }));

        Assert.Empty(store.Get(user).Groups);
        Assert.Equal(0, store.Get(user).Revision);
    }

    [Fact]
    public void ConcurrentUpdatesAreNotLost()
    {
        var user = Guid.NewGuid();
        var store = new GroupStore(_directory);

        Parallel.For(0, 50, i => store.Update(user, doc =>
        {
            doc.Groups.Add(new ChannelGroup { Id = Guid.NewGuid(), Name = "G" + i });
            return true;
        }));

        Assert.Equal(50, new GroupStore(_directory).Get(user).Groups.Count);
        Assert.Equal(50, store.Get(user).Revision);
    }

    [Fact]
    public void DeleteRemovesUserData()
    {
        var user = Guid.NewGuid();
        var store = new GroupStore(_directory);
        store.Update(user, doc => { doc.Groups.Add(new ChannelGroup { Name = "X" }); return true; });

        store.Delete(user);

        Assert.Empty(new GroupStore(_directory).Get(user).Groups);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory)).Where(f => f.EndsWith(".json", StringComparison.Ordinal)));
    }
}
