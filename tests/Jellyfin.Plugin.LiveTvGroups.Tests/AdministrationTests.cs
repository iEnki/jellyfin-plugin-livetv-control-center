using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using Jellyfin.Plugin.LiveTvGroups.Web;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class AdministrationTests
{
    [Fact]
    public void SharedVisibilityHonorsAllowAndDenyListsAndAdminRetainsManagementAccess()
    {
        using var f = new Fixture();
        f.Store.UpdateAdministration(config =>
        {
            config.Mode = "shared";
            config.Groups =
            [
                new() { Id = Guid.NewGuid(), Name = "Everyone" },
                new() { Id = Guid.NewGuid(), Name = "Excluded", DeniedUserIds = [f.Alice.Id] },
                new() { Id = Guid.NewGuid(), Name = "Selected", VisibleToAllUsers = false, AllowedUserIds = [f.Alice.Id] },
                new() { Id = Guid.NewGuid(), Name = "DenyWins", VisibleToAllUsers = false, AllowedUserIds = [f.Alice.Id], DeniedUserIds = [f.Alice.Id] }
            ];
            return true;
        });
        Assert.Equal(["Everyone", "Selected"], f.Groups.GetGroups(f.Alice).Select(g => g.Name));
        Assert.Equal(["Everyone", "Excluded"], f.Groups.GetGroups(f.Bob).Select(g => g.Name));
        Assert.Equal(4, f.Groups.GetGroups(f.Admin).Count);
        f.Alice.SetPermission(PermissionKind.EnableLiveTvAccess, false);
        Assert.Empty(f.Groups.GetGroups(f.Alice));
        f.Alice.SetPermission(PermissionKind.EnableLiveTvAccess, true);
        f.Alice.SetPermission(PermissionKind.IsDisabled, true);
        Assert.Empty(f.Groups.GetGroups(f.Alice));
    }

    [Fact]
    public async Task PersonalGroupsRemainIsolatedAndSurviveModeSwitchAndAdminImport()
    {
        using var f = new Fixture();
        f.Groups.Update(f.Admin, doc => { doc.Groups.Add(new() { Id = f.Group, Name = "Admin Personal" }); return true; });
        f.Groups.Update(f.Alice, doc => { doc.Groups.Add(new() { Id = Guid.NewGuid(), Name = "Alice Personal" }); return true; });
        Assert.Equal("Alice Personal", Assert.Single(f.Groups.GetGroups(f.Alice)).Name);
        var api = f.Api(f.Admin);
        Assert.IsType<NoContentResult>(await api.SetAdministration(new() { Mode = "shared", ImportPersonalGroups = true }));
        Assert.Equal("Admin Personal", Assert.Single(f.Groups.GetGroups(f.Alice)).Name);
        Assert.IsType<NoContentResult>(await api.SetAdministration(new() { Mode = "shared", ImportPersonalGroups = true }));
        Assert.Single(f.Store.GetAdministration().Groups);
        Assert.NotSame(f.Store.Get(f.Admin.Id).Groups[0], f.Store.GetAdministration().Groups[0]);
        Assert.IsType<NoContentResult>(await api.SetAdministration(new() { Mode = "personal" }));
        Assert.Equal("Alice Personal", Assert.Single(f.Groups.GetGroups(f.Alice)).Name);
        Assert.Equal("Admin Personal", Assert.Single(f.Groups.GetGroups(f.Admin)).Name);
    }

    [Fact]
    public void NormalUserCannotEditSharedGroupsEvenIfModeChangesAfterPrecheck()
    {
        using var f = new Fixture();
        Assert.True(f.Groups.CanManage(f.Alice));
        f.Shared();
        Assert.False(f.Groups.CanManage(f.Alice));
        Assert.Throws<UnauthorizedAccessException>(() => f.Groups.Update(f.Alice, doc => { doc.Groups.Clear(); return true; }));
        var api = f.Api(f.Alice);
        Assert.IsType<ForbidResult>(api.CreateGroup(new() { Name = "Unauthorized" }).Result);
        Assert.IsType<ForbidResult>(api.RenameGroup(f.Group, new() { Name = "Unauthorized" }));
        Assert.IsType<ForbidResult>(api.DeleteGroup(f.Group));
        Assert.IsType<ForbidResult>(api.SetGroupOrder([f.Group]));
        Assert.IsType<ForbidResult>(api.SetGroupChannels(f.Group, []));
        Assert.Equal("Shared", Assert.Single(f.Store.GetAdministration().Groups).Name);
        f.Groups.Update(f.Admin, doc => { doc.Groups[0].Name = "Admin Edit"; return true; });
        Assert.Equal("Admin Edit", Assert.Single(f.Store.GetAdministration().Groups).Name);
    }

    [Fact]
    public async Task AdministrationEndpointsRequireAdminAndRejectUnknownUsersAndModes()
    {
        using var f = new Fixture();
        f.Shared();
        var api = f.Api(f.Alice);
        Assert.IsType<ForbidResult>(api.GetAdministration());
        Assert.IsType<ForbidResult>(await api.SetAdministration(new() { Mode = "personal" }));
        Assert.IsType<ForbidResult>(api.SetGroupAccess(f.Group, new() { DeniedUserIds = [f.Bob.Id] }));
        api = f.Api(f.Admin);
        Assert.IsType<BadRequestObjectResult>(await api.SetAdministration(new() { Mode = "invalid" }));
        Assert.IsType<BadRequestObjectResult>(await api.SetAdministration(new() { Mode = "personal", ImportPersonalGroups = true }));
        Assert.IsType<BadRequestObjectResult>(api.SetGroupAccess(f.Group, new() { AllowedUserIds = [Guid.NewGuid()] }));
        Assert.IsType<NotFoundResult>(api.SetGroupAccess(Guid.NewGuid(), new()));
        foreach (var method in new[] { "GetAdministration", "SetAdministration", "SetGroupAccess" })
        {
            Assert.Contains(typeof(GroupsController).GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
                a => a.Policy == MediaBrowser.Common.Api.Policies.RequiresElevation);
        }
    }

    [Fact]
    public void DirectGroupApiAndPreferencesDoNotRevealDeniedGroups()
    {
        using var f = new Fixture();
        f.Shared();
        f.Store.Update(f.Alice.Id, doc =>
        {
            doc.Preferences.HiddenGroupIds = [f.Group];
            doc.Preferences.DefaultGroupId = f.Group;
            doc.Preferences.LastGroupId = f.Group;
            return true;
        });
        Assert.IsType<NoContentResult>(f.Api(f.Admin).SetGroupAccess(f.Group, new() { DeniedUserIds = [f.Alice.Id] }));
        var api = f.Api(f.Alice);
        var list = Assert.IsType<OkObjectResult>(api.GetGroups().Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<GroupDto>>(list.Value));
        Assert.IsType<NotFoundResult>(api.GetGroupChannels(f.Group).Result);
        var result = Assert.IsType<OkObjectResult>(api.GetPreferences().Result);
        var preferences = Assert.IsType<GroupPreferences>(result.Value);
        Assert.Empty(preferences.HiddenGroupIds);
        Assert.Null(preferences.DefaultGroupId);
        Assert.Null(preferences.LastGroupId);
        Assert.Equal([f.Group], f.Store.Get(f.Alice.Id).Preferences.HiddenGroupIds);
        Assert.IsType<BadRequestObjectResult>(api.SetPreferences(new() { DefaultGroupId = f.Group }));
    }

    [Fact]
    public void CentralStorePersistsAndFailedUpdatesDoNotChangeSnapshotOrRevision()
    {
        using var f = new Fixture();
        f.Shared();
        var before = f.Store.GetAdministration();
        Assert.Throws<InvalidOperationException>(() => f.Store.UpdateAdministration<bool>(config =>
        {
            config.Mode = "personal";
            config.Groups.Clear();
            throw new InvalidOperationException();
        }));
        Assert.Same(before, f.Store.GetAdministration());
        var reloaded = new GroupStore(f.Directory).GetAdministration();
        Assert.Equal("shared", reloaded.Mode);
        Assert.Equal(before.Revision, reloaded.Revision);
        Assert.Equal(f.Group, Assert.Single(reloaded.Groups).Id);
        Assert.DoesNotContain(Guid.Empty, f.Store.GetUserIds());
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ltvg-admin-" + Guid.NewGuid().ToString("N"));
        public User Admin { get; } = NewUser("Admin", true);
        public User Alice { get; } = NewUser("Alice");
        public User Bob { get; } = NewUser("Bob");
        public Guid Group { get; } = Guid.NewGuid();
        public GroupStore Store { get; }
        public GroupService Groups { get; }
        private readonly ServiceProvider _services;
        private readonly IUserManager _users;
        private readonly PlaylistSyncService _sync;
        private readonly GroupArtworkService _artwork;
        public Fixture()
        {
            Store = new(Directory);
            var all = new[] { Admin, Alice, Bob };
            _users = InterfaceStub.Create<IUserManager>((method, args) => method.Name switch
            {
                "GetUserById" => all.FirstOrDefault(u => Equals(u.Id, args![0])),
                "GetUsers" => all,
                _ => throw new NotSupportedException(method.Name)
            });
            _services = new ServiceCollection().AddSingleton(Store).AddSingleton<GroupService>().AddSingleton(_users).BuildServiceProvider();
            Groups = _services.GetRequiredService<GroupService>();
            _sync = new(Groups, _services, NullLogger<PlaylistSyncService>.Instance);
            _artwork = new(Store, _services, NullLogger<GroupArtworkService>.Instance);
        }

        public void Shared() => Store.UpdateAdministration(config =>
        {
            config.Mode = "shared";
            config.Groups = [new() { Id = Group, Name = "Shared" }];
            return true;
        });

        public GroupsController Api(User user) => new(Groups, _users, null!, new WebInjectionStatus(), _sync, _artwork)
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", user.Id.ToString())], "test"))
                }
            }
        };

        private static User NewUser(string name, bool admin = false)
        {
            var user = new User(name, "auth", "reset");
            user.SetPermission(PermissionKind.EnableLiveTvAccess, true);
            user.SetPermission(PermissionKind.IsAdministrator, admin);
            return user;
        }

        public void Dispose()
        {
            _services.Dispose();
            if (System.IO.Directory.Exists(Directory)) { System.IO.Directory.Delete(Directory, true); }
        }
    }
}
