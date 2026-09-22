using System;
using System.IO;
using System.Linq;
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
using Jellyfin.Plugin.LiveTvGroups.Web;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public sealed class GroupArtworkTests
{
    [Fact]
    public async Task DefaultsAndCustomImagesAreValidatedReplacedScopedCopiedAndReset()
    {
        using var fixture = new Fixture();
        Assert.True(File.Exists(fixture.Artwork.RootLogoPath));
        Assert.True(File.Exists(fixture.Artwork.EpgPath));
        Assert.True(File.Exists(fixture.Artwork.AllChannelsPath));
        Assert.True(File.Exists(fixture.Artwork.GroupPath));
        Assert.True(File.Exists(fixture.Artwork.ProgramListPath));
        Assert.Equal(5, new[]
        {
            fixture.Artwork.RootLogoPath,
            fixture.Artwork.EpgPath,
            fixture.Artwork.AllChannelsPath,
            fixture.Artwork.GroupPath,
            fixture.Artwork.ProgramListPath
        }.Distinct(StringComparer.Ordinal).Count());

        var png = await fixture.Artwork.SaveCustomImageAsync(
            fixture.Admin.Id,
            false,
            fixture.GroupId,
            new MemoryStream(Png()),
            "image/png",
            CancellationToken.None);
        Assert.EndsWith(".png", png, StringComparison.Ordinal);
        Assert.True(fixture.Artwork.HasCustomImage(fixture.Admin.Id, false, fixture.GroupId));
        Assert.False(fixture.Artwork.HasCustomImage(fixture.Other.Id, false, fixture.GroupId));
        Assert.False(fixture.Artwork.HasCustomImage(fixture.Admin.Id, true, fixture.GroupId));

        var jpeg = await fixture.Artwork.SaveCustomImageAsync(
            fixture.Admin.Id,
            false,
            fixture.GroupId,
            new MemoryStream(Jpeg()),
            "image/jpeg",
            CancellationToken.None);
        Assert.EndsWith(".jpg", jpeg, StringComparison.Ordinal);
        Assert.False(File.Exists(png));

        fixture.Artwork.CopyPersonalToShared(fixture.Admin.Id, [fixture.GroupId]);
        Assert.True(fixture.Artwork.HasCustomImage(fixture.Admin.Id, true, fixture.GroupId));
        Assert.True(fixture.Artwork.DeleteCustomImage(fixture.Admin.Id, false, fixture.GroupId));
        Assert.Equal(fixture.Artwork.GroupPath, fixture.Artwork.GetGroupImage(fixture.Admin.Id, false, fixture.GroupId));
        Assert.False(fixture.Artwork.DeleteCustomImage(fixture.Admin.Id, false, fixture.GroupId));
    }

    [Fact]
    public async Task InvalidContentMismatchDecoderFailureAndSizeLimitAreRejected()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Artwork.SaveCustomImageAsync(
            fixture.Admin.Id, false, fixture.GroupId, new MemoryStream([1, 2, 3]), "image/png", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Artwork.SaveCustomImageAsync(
            fixture.Admin.Id, false, fixture.GroupId, new MemoryStream(Png()), "image/jpeg", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Artwork.SaveCustomImageAsync(
            fixture.Admin.Id, false, fixture.GroupId, new MemoryStream(Png()), "application/octet-stream", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Artwork.SaveCustomImageAsync(
            fixture.Admin.Id, false, fixture.GroupId, new MemoryStream(new byte[GroupArtworkService.MaxImageBytes + 1]), "image/png", CancellationToken.None));

        fixture.Decode = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Artwork.SaveCustomImageAsync(
            fixture.Admin.Id, false, fixture.GroupId, new MemoryStream(Png()), "image/png", CancellationToken.None));
    }

    [Fact]
    public async Task ControllerUpdatesRevisionAndHonorsPersonalAndCentralPermissions()
    {
        using var fixture = new Fixture();
        fixture.AddPersonalGroup();
        var admin = fixture.Api(fixture.Admin, Png(), "image/png");
        var saved = Assert.IsType<OkObjectResult>((await admin.SetGroupImage(fixture.GroupId, CancellationToken.None)).Result);
        var dto = Assert.IsType<GroupDto>(saved.Value);
        Assert.True(dto.HasCustomImage);
        Assert.Equal(1, dto.ArtworkRevision);
        Assert.IsType<PhysicalFileResult>(admin.GetGroupImage(fixture.GroupId));

        var reset = await admin.ResetGroupImage(fixture.GroupId, CancellationToken.None);
        Assert.IsType<NoContentResult>(reset);
        Assert.False(fixture.Artwork.HasCustomImage(fixture.Admin.Id, false, fixture.GroupId));
        Assert.Equal(2, fixture.Store.Get(fixture.Admin.Id).Groups.Single().ArtworkRevision);
        Assert.IsType<NotFoundResult>((await admin.SetGroupImage(Guid.NewGuid(), CancellationToken.None)).Result);

        admin = fixture.Api(fixture.Admin, Png(), "image/png");
        Assert.IsType<OkObjectResult>((await admin.SetGroupImage(fixture.GroupId, CancellationToken.None)).Result);
        admin = fixture.Api(fixture.Admin, Png(), "image/png");
        Assert.IsType<NoContentResult>(await admin.SetAdministration(new() { Mode = "shared", ImportPersonalGroups = true }));
        Assert.True(fixture.Artwork.HasCustomImage(fixture.Admin.Id, true, fixture.GroupId));        var normal = fixture.Api(fixture.Other, Png(), "image/png");
        Assert.IsType<ForbidResult>((await normal.SetGroupImage(fixture.GroupId, CancellationToken.None)).Result);
        admin = fixture.Api(fixture.Admin, Png(), "image/png");
        Assert.IsType<OkObjectResult>((await admin.SetGroupImage(fixture.GroupId, CancellationToken.None)).Result);
        Assert.True(fixture.Artwork.HasCustomImage(fixture.Admin.Id, true, fixture.GroupId));
    }

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly IUserManager _users;
        private readonly PlaylistSyncService _sync;
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ltvg-artwork-" + Guid.NewGuid().ToString("N"));
        public User Admin { get; } = User("Admin", true);
        public User Other { get; } = User("Other");
        public Guid GroupId { get; } = Guid.NewGuid();
        public GroupStore Store { get; }
        public GroupService Groups { get; }
        public GroupArtworkService Artwork { get; }
        public bool Decode { get; set; } = true;

        public Fixture()
        {
            Store = new GroupStore(Path.Combine(Directory, "users"));
            _users = InterfaceStub.Create<IUserManager>((method, args) => method.Name switch
            {
                "GetUserById" => new[] { Admin, Other }.FirstOrDefault(user => Equals(user.Id, args![0])),
                "GetUsers" => new[] { Admin, Other },
                _ => throw new NotSupportedException(method.Name)
            });
            var encoder = InterfaceStub.Create<IImageEncoder>((method, _) => method.Name switch
            {
                "GetImageSize" => Decode ? new ImageDimensions(1024, 1024) : throw new InvalidDataException("decoder"),
                _ => throw new NotSupportedException(method.Name)
            });
            _services = new ServiceCollection()
                .AddSingleton(Store)
                .AddSingleton(_users)
                .AddSingleton(encoder)
                .AddSingleton<GroupService>()
                .BuildServiceProvider();
            Groups = _services.GetRequiredService<GroupService>();
            Artwork = new GroupArtworkService(Store, _services, NullLogger<GroupArtworkService>.Instance);
            _sync = new PlaylistSyncService(Groups, _services, NullLogger<PlaylistSyncService>.Instance);
        }

        public void AddPersonalGroup() => Groups.Update(Admin, document =>
        {
            document.Groups.Add(new ChannelGroup { Id = GroupId, Name = "Crime" });
            return true;
        });

        public GroupsController Api(User user, byte[] body, string contentType)
        {
            var http = new DefaultHttpContext
            {
                RequestServices = _services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", user.Id.ToString())], "test"))
            };
            http.Request.Body = new MemoryStream(body);
            http.Request.ContentLength = body.Length;
            http.Request.ContentType = contentType;
            return new GroupsController(Groups, _users, null!, new WebInjectionStatus(), _sync, Artwork)
            {
                ControllerContext = new ControllerContext { HttpContext = http }
            };
        }

        private static User User(string name, bool administrator = false)
        {
            var user = new User(name, "auth", "reset");
            user.SetPermission(PermissionKind.EnableLiveTvAccess, true);
            user.SetPermission(PermissionKind.IsAdministrator, administrator);
            return user;
        }

        public void Dispose()
        {
            _services.Dispose();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
