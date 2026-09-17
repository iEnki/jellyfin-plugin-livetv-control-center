using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Jellyfin.Plugin.LiveTvGroups.Model;
using Jellyfin.Plugin.LiveTvGroups.Services;
using Jellyfin.Plugin.LiveTvGroups.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class PlayerTests
{
    [Fact]
    public void AndroidTvWithoutRemoteFlagIsDiscoveredButUnconnectedAndOtherUsersAreNot()
    {
        using var f = new Fixture();
        var tv = f.Target();
        Assert.False(tv.SupportsRemoteControl);
        Assert.True(Assert.Single(f.Players.GetPlayers(f.User)).UsesAndroidTvDiscoveryFallback);
        f.Target(client: "Some client", name: "Fire TV");
        f.Target(user: f.Other);
        f.Target(connected: false);
        Assert.Single(f.Players.GetPlayers(f.User));
        tv.SessionControllers = [Controller(false, false)];
        Assert.Empty(f.Players.GetPlayers(f.User));
    }

    [Fact]
    public void StandardRemoteClientsRequireCapabilityAndActiveTransport()
    {
        using var f = new Fixture();
        var target = f.Target(client: "Jellyfin Web");
        Assert.Empty(f.Players.GetPlayers(f.User));
        target.Capabilities = new ClientCapabilities { SupportsMediaControl = true };
        target.SessionControllers = [Controller(true, true)];
        Assert.Single(f.Players.GetPlayers(f.User));
        target.SessionControllers = [];
        Assert.True(target.IsActive); // This alone must NOT make it discoverable.
        Assert.Empty(f.Players.GetPlayers(f.User));
    }

    [Fact]
    public async Task DeviceIdIsResolvedForEveryPlayAndNewestEligibleSessionWins()
    {
        using var f = new Fixture();
        var old = f.Target();
        var fresh = f.Target();
        fresh.DeviceId = old.DeviceId;
        fresh.LastActivityDate = old.LastActivityDate.AddMinutes(1);
        Assert.Single(f.Players.GetPlayers(f.User));
        await f.Players.Play(f.User, f.Caller, old.DeviceId, f.Channel.Id, CancellationToken.None);
        Assert.Equal(fresh.Id, f.SentTarget);
        Assert.Equal(f.Caller.Id, f.SentCaller);
        Assert.Equal(PlayCommand.PlayNow, f.Command!.PlayCommand);
        Assert.Equal(f.Channel.Id, Assert.Single(f.Command.ItemIds));
        Assert.Equal(f.User.Id, f.Command.ControllingUserId);
        f.Sessions.Remove(fresh);
        await f.Players.Play(f.User, f.Caller, old.DeviceId, f.Channel.Id, CancellationToken.None);
        Assert.Equal(old.Id, f.SentTarget);
    }

    [Fact]
    public async Task OtherUserRequiresRemotePermissionAndBothUsersChannelAccess()
    {
        using var f = new Fixture();
        var target = f.Target(user: f.Other);
        await Assert.ThrowsAsync<PlayerUnavailableException>(() => f.Play(target));
        f.User.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, true);
        await f.Play(target);
        f.OtherChannels.Clear();
        await Assert.ThrowsAsync<SecurityException>(() => f.Play(target));
        f.OtherChannels.Add(f.Channel);
        f.Other.SetPermission(PermissionKind.EnableLiveTvAccess, false);
        await Assert.ThrowsAsync<SecurityException>(() => f.Play(target));
    }

    [Fact]
    public async Task RevokedRightsForgedCallerAndOfflineDeviceNeverSend()
    {
        using var f = new Fixture();
        var target = f.Target();
        f.Caller.UserId = f.Other.Id;
        await Assert.ThrowsAsync<SecurityException>(() => f.Play(target));
        f.Caller.UserId = f.User.Id;
        f.UserChannels.Clear();
        await Assert.ThrowsAsync<SecurityException>(() => f.Play(target));
        f.UserChannels.Add(f.Channel);
        f.User.SetPermission(PermissionKind.EnableMediaPlayback, false);
        await Assert.ThrowsAsync<SecurityException>(() => f.Play(target));
        f.User.SetPermission(PermissionKind.EnableMediaPlayback, true);
        f.User.SetPermission(PermissionKind.IsDisabled, true);
        await Assert.ThrowsAsync<SecurityException>(() => f.Play(target));
        f.User.SetPermission(PermissionKind.IsDisabled, false);
        f.Sessions.Remove(target);
        await Assert.ThrowsAsync<PlayerUnavailableException>(() => f.Play(target));
        Assert.Null(f.Command);
    }

    [Fact]
    public async Task UnknownChannelAndEmptyControllingSessionCannotUsePrivilegedPath()
    {
        using var f = new Fixture();
        var target = f.Target();
        await Assert.ThrowsAsync<SecurityException>(() =>
            f.Players.Play(f.User, f.Caller, target.DeviceId, Guid.NewGuid(), CancellationToken.None));
        f.Caller.Id = string.Empty;
        await Assert.ThrowsAsync<SecurityException>(() => f.Play(target));
        Assert.Null(f.Command);
    }

    [Fact]
    public void DiscoveryRejectsDisabledAndPlaybackRestrictedCallersAndPublicSessions()
    {
        using var f = new Fixture();
        var target = f.Target();
        target.UserId = Guid.Empty;
        Assert.Empty(f.Players.GetPlayers(f.User));
        f.User.SetPermission(PermissionKind.IsDisabled, true);
        Assert.Throws<SecurityException>(() => f.Players.GetPlayers(f.User));
        f.User.SetPermission(PermissionKind.IsDisabled, false);
        f.User.SetPermission(PermissionKind.EnableMediaPlayback, false);
        Assert.Throws<SecurityException>(() => f.Players.GetPlayers(f.User));
    }

    [Fact]
    public async Task CancelledRequestDoesNotSend()
    {
        using var f = new Fixture();
        var target = f.Target();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            f.Players.Play(f.User, f.Caller, target.DeviceId, f.Channel.Id, new CancellationToken(true)));
        Assert.Null(f.Command);
    }

    [Fact]
    public async Task ControllerUsesTokenSessionAndMapsFailures()
    {
        using var f = new Fixture();
        var target = f.Target();
        Assert.IsType<NoContentResult>(await f.Api.Play(target.DeviceId, f.Channel.Id));
        Assert.Equal("token", f.ResolvedToken);
        f.Command = null;
        f.Caller.UserId = f.Other.Id;
        Assert.IsType<ForbidResult>(await f.Api.Play(target.DeviceId, f.Channel.Id));
        f.Caller.UserId = f.User.Id;
        f.Auth.IsApiKey = true;
        Assert.IsType<UnauthorizedResult>(await f.Api.Play(target.DeviceId, f.Channel.Id));
        f.Auth.IsApiKey = false;
        f.Sessions.Remove(target);
        Assert.IsType<ConflictObjectResult>(await f.Api.Play(target.DeviceId, f.Channel.Id));
        Assert.Null(f.Command);
    }

    [Fact]
    public void TargetPreferencesPersistPerUserAndCannotGuessUnauthorizedDevice()
    {
        using var f = new Fixture();
        var target = f.Target(name: "Wohnzimmer");
        Assert.IsType<NoContentResult>(f.Api.SetPreference(new() { DeviceId = target.DeviceId }));
        var preferences = new GroupStore(f.Directory).Get(f.User.Id).Preferences;
        Assert.Equal(target.DeviceId, preferences.PreferredTargetDeviceId);
        Assert.Equal("Wohnzimmer", preferences.PreferredTargetDeviceName);
        Assert.Null(f.Store.Get(f.Other.Id).Preferences.PreferredTargetDeviceId);
        Assert.IsType<ConflictObjectResult>(f.Api.SetPreference(new() { DeviceId = f.Target(user: f.Other).DeviceId }));
        Assert.IsType<NoContentResult>(f.Api.SetPreference(new() { DeviceId = null }));
        Assert.Null(f.Store.Get(f.User.Id).Preferences.PreferredTargetDeviceId);
    }

    [Fact]
    public void PagePreferencesCannotOverwriteTargetFromStaleOrOldClients()
    {
        using var f = new Fixture();
        var target = f.Target();
        f.Api.SetPreference(new() { DeviceId = target.DeviceId });
        var controller = new GroupsController(f.Groups, f.Users, null!, null!, null!);
        controller.ControllerContext = f.Api.ControllerContext;
        Assert.IsType<NoContentResult>(controller.SetPreferences(new GroupPreferences { Zoom = 8 }));
        Assert.Equal(target.DeviceId, f.Store.Get(f.User.Id).Preferences.PreferredTargetDeviceId);
        Assert.Equal(8, f.Store.Get(f.User.Id).Preferences.Zoom);
    }

    [Fact]
    public void DevVersionIsStrictlyBetweenCurrentAndNextRelease()
    {
        var assembly = typeof(Plugin).Assembly;
        if (!assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Contains("-dev.remote-live-tv", StringComparison.Ordinal)) return;
        var version = assembly.GetName().Version!;
        Assert.True(version > new Version(0, 3, 1, 0));
        Assert.True(version < new Version(0, 3, 2, 0));
    }

    private static ISessionController Controller(bool active, bool media)
        => InterfaceStub.Create<ISessionController>((method, _) => method.Name switch
        {
            "get_IsSessionActive" => active,
            "get_SupportsMediaControl" => media,
            _ => throw new NotSupportedException(method.Name)
        });

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ltvg-players-" + Guid.NewGuid().ToString("N"));
        public User User { get; } = NewUser("Robert");
        public User Other { get; } = NewUser("Other");
        public LiveTvChannel Channel { get; } = new() { Id = Guid.NewGuid(), Name = "ORF 1" };
        public List<BaseItem> UserChannels { get; } = [];
        public List<BaseItem> OtherChannels { get; } = [];
        public List<SessionInfo> Sessions { get; } = [];
        public SessionInfo Caller { get; }
        public PlayRequest? Command;
        public string? SentCaller, SentTarget, ResolvedToken;
        public AuthorizationInfo Auth { get; } = new() { Token = "token", DeviceId = "phone" };
        public IUserManager Users { get; }
        public ISessionManager Manager { get; }
        public GroupStore Store { get; }
        public GroupService Groups { get; }
        public PlayerService Players { get; }
        public PlayersController Api { get; }
        private readonly ServiceProvider _provider;

        public Fixture()
        {
            UserChannels.Add(Channel); OtherChannels.Add(Channel);
            Users = InterfaceStub.Create<IUserManager>((method, args) => method.Name == "GetUserById"
                ? Equals(args![0], User.Id) ? User : Equals(args[0], Other.Id) ? Other : null
                : throw new NotSupportedException(method.Name));
            Manager = InterfaceStub.Create<ISessionManager>((method, args) =>
            {
                if (method.Name == "get_Sessions") return Sessions.ToArray();
                if (method.Name == "GetSessionByAuthenticationToken") { ResolvedToken = (string)args![0]!; return Task.FromResult(Caller!); }
                if (method.Name == "SendPlayCommand")
                {
                    SentCaller = (string)args![0]!; SentTarget = (string)args[1]!; Command = (PlayRequest)args[2]!;
                    return Task.CompletedTask;
                }
                throw new NotSupportedException(method.Name);
            });
            Caller = new SessionInfo(Manager, NullLogger.Instance) { Id = "phone-session", DeviceId = "phone", UserId = User.Id };
            Store = new GroupStore(Directory);
            var services = new ServiceCollection().AddSingleton(Store).AddSingleton<GroupService>();
            services.AddSingleton(InterfaceStub.Create<ILiveTvManager>((method, args) =>
            {
                if (method.Name != "GetInternalChannels") throw new NotSupportedException(method.Name);
                var channels = ((MediaBrowser.Model.LiveTv.LiveTvChannelQuery)args![0]!).UserId == User.Id ? UserChannels : OtherChannels;
                return new QueryResult<BaseItem>(0, channels.Count, channels);
            }));
            _provider = services.BuildServiceProvider();
            Groups = _provider.GetRequiredService<GroupService>();
            Players = new PlayerService(Manager, Users, Groups);
            var authorization = InterfaceStub.Create<IAuthorizationContext>((method, _) =>
                method.Name == "GetAuthorizationInfo" ? Task.FromResult(Auth) : throw new NotSupportedException(method.Name));
            Api = new PlayersController(Players, Groups, Users, Manager, authorization);
            Api.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", User.Id.ToString())], "test"))
                }
            };
        }

        public SessionInfo Target(User? user = null, string client = "Jellyfin Android TV", string name = "TV", bool connected = true)
        {
            var target = new SessionInfo(Manager, NullLogger.Instance)
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = Guid.NewGuid().ToString("N"),
                DeviceName = name,
                Client = client,
                UserId = (user ?? User).Id,
                LastActivityDate = DateTime.UtcNow,
                SessionControllers = connected ? [Controller(true, false)] : []
            };
            Sessions.Add(target); return target;
        }

        public Task Play(SessionInfo target) => Players.Play(User, Caller, target.DeviceId, Channel.Id, CancellationToken.None);
        private static User NewUser(string name)
        {
            var user = new User(name, "auth", "password");
            user.SetPermission(PermissionKind.EnableLiveTvAccess, true);
            user.SetPermission(PermissionKind.EnableMediaPlayback, true);
            user.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, false);
            return user;
        }

        public void Dispose()
        {
            _provider.Dispose();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
