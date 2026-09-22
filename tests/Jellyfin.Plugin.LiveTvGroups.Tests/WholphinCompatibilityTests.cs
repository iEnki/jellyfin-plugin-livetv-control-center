using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LiveTvGroups.Api;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.LiveTvGroups.Tests;

public class WholphinCompatibilityTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _pluginId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    [Theory]
    [InlineData("GetUserViews")]
    [InlineData("GetUserViewsLegacy")]
    public async Task WholphinReceivesSupportedRootAndGroupFolders(string viewsAction)
    {
        var library = Library();
        var filter = Filter(library, true);
        var http = Http("Wholphin");
        var root = new BaseItemDto { Id = _pluginId, Name = "Live-TV Control Center", Type = BaseItemKind.Channel, ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = "root-tag" } };
        var views = await Invoke(filter, http, typeof(Jellyfin.Api.Controllers.UserViewsController), viewsAction,
            new() { ["userId"] = _userId }, new([root]));

        var rewrittenRoot = Assert.Single(views.Items);
        Assert.Equal(BaseItemKind.CollectionFolder, rewrittenRoot.Type);
        Assert.Equal(CollectionType.folders, rewrittenRoot.CollectionType);
        Assert.True(rewrittenRoot.IsFolder);
        Assert.Equal("root-tag", rewrittenRoot.ImageTags[ImageType.Primary]);

        var group = new BaseItemDto { Id = Guid.NewGuid(), Name = "Crime", Type = BaseItemKind.ChannelFolderItem, IsFolder = true, ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = "group-tag" } };
        var children = await Invoke(filter, http, typeof(Jellyfin.Api.Controllers.ItemsController), "GetItems",
            new() { ["userId"] = _userId, ["parentId"] = _pluginId }, new([group]));
        var rewrittenGroup = Assert.Single(children.Items);
        Assert.Equal(BaseItemKind.Folder, rewrittenGroup.Type);
        Assert.Equal(CollectionType.unknown, rewrittenGroup.CollectionType);
        Assert.Equal("group-tag", rewrittenGroup.ImageTags[ImageType.Primary]);
    }

    [Fact]
    public async Task GroupSelectionShowsOneWholphinConfirmationAndHidesMediaTiles()
    {
        var groupId = Guid.NewGuid();
        var parent = new Folder
        {
            Id = Guid.NewGuid(),
            ChannelId = _pluginId,
            ExternalId = GroupsChannel.GetFolderExternalId(groupId)
        };
        var filter = Filter(Library(parent), true);
        var http = Http("Wholphin");
        http.Request.Headers.AcceptLanguage = "en";
        http.Items[NativeGuideActionFilter.ActionResultKey] = new NativeGuideActionResult(groupId, "Crime", false, true);
        var response = new QueryResult<BaseItemDto>(
        [
            new() { Id = Guid.NewGuid(), Name = "Native guide", Type = BaseItemKind.ChannelFolderItem, IsFolder = true },
            new() { Id = Guid.NewGuid(), Name = "Program list", Type = BaseItemKind.ChannelFolderItem, IsFolder = true },
            new() { Id = Guid.NewGuid(), Name = "Unplayable channel", IsFolder = false }
        ]);

        var result = await Invoke(filter, http, typeof(Jellyfin.Api.Controllers.ItemsController), "GetItems",
            new() { ["userId"] = _userId, ["parentId"] = parent.Id }, response);

        var confirmation = Assert.Single(result.Items);
        Assert.Equal(BaseItemKind.Folder, confirmation.Type);
        Assert.Equal(CollectionType.unknown, confirmation.CollectionType);
        Assert.Contains("Crime", confirmation.Name, StringComparison.Ordinal);
        Assert.Contains("Live TV", confirmation.Overview, StringComparison.Ordinal);
        Assert.Equal(1, result.TotalRecordCount);
    }

    [Fact]
    public async Task DisabledWholphinTargetHidesBrokenEntryAndCannotRunActionFilter()
    {
        var filter = Filter(Library(), false);
        var http = Http("Wholphin");
        var other = new BaseItemDto { Id = Guid.NewGuid(), Name = "Movies", Type = BaseItemKind.CollectionFolder };
        var response = new QueryResult<BaseItemDto>(
            [new() { Id = _pluginId, Name = "Live-TV Control Center", Type = BaseItemKind.Channel }, other]);

        var result = await Invoke(filter, http, typeof(Jellyfin.Api.Controllers.UserViewsController), "GetUserViews",
            new() { ["userId"] = _userId }, response);

        Assert.Same(other, Assert.Single(result.Items));
        Assert.False(NativeGuideActionFilter.SupportsClient("Wholphin", false));
        Assert.True(NativeGuideActionFilter.SupportsClient("Wholphin", true));
        Assert.True(NativeGuideActionFilter.SupportsClient("Jellyfin for Android TV", false));
    }

    [Theory]
    [InlineData("Android TV")]
    [InlineData("Jellyfin Web")]
    [InlineData("Wholphin Web")]
    public async Task OtherClientsRemainUnchanged(string client)
    {
        var filter = Filter(Library(), true);
        var original = new BaseItemDto { Id = _pluginId, Name = "Live-TV Control Center", Type = BaseItemKind.Channel };
        var result = await Invoke(filter, Http(client), typeof(Jellyfin.Api.Controllers.UserViewsController), "GetUserViews",
            new() { ["userId"] = _userId }, new([original]));
        Assert.Equal(BaseItemKind.Channel, Assert.Single(result.Items).Type);
    }

    [Fact]
    public async Task ForeignUserAndApiKeyNeverReceiveCompatibilityRewrite()
    {
        var filter = Filter(Library(), true);
        var apiKey = Http("Wholphin", true);
        var foreign = Http("Wholphin");
        foreach (var (http, requestedUser) in new[] { (apiKey, _userId), (foreign, Guid.NewGuid()) })
        {
            var original = new BaseItemDto { Id = _pluginId, Type = BaseItemKind.Channel };
            var result = await Invoke(filter, http, typeof(Jellyfin.Api.Controllers.UserViewsController), "GetUserViews",
                new() { ["userId"] = requestedUser }, new([original]));
            Assert.Equal(BaseItemKind.Channel, Assert.Single(result.Items).Type);
        }
    }

    private WholphinCompatibilityFilter Filter(ILibraryManager library, bool enabled)
        => new(library, NullLogger<WholphinCompatibilityFilter>.Instance, () => enabled);

    private ILibraryManager Library(Folder? parent = null)
        => InterfaceStub.Create<ILibraryManager>((method, arguments) => method.Name switch
        {
            "GetNewItemId" => _pluginId,
            "GetItemById" => arguments is not null && (Guid)arguments[0]! == parent?.Id ? parent : null,
            _ => null
        });

    private DefaultHttpContext Http(string client, bool apiKey = false)
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new("Jellyfin-UserId", _userId.ToString()),
            new("Jellyfin-DeviceId", "wholphin-tv"),
            new("Jellyfin-Client", client),
            new("Jellyfin-IsApiKey", apiKey ? "True" : "False")
        ], "fixture"));
        return http;
    }

    private static async Task<QueryResult<BaseItemDto>> Invoke(
        WholphinCompatibilityFilter filter,
        DefaultHttpContext http,
        Type controller,
        string action,
        Dictionary<string, object?> arguments,
        QueryResult<BaseItemDto> response)
    {
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = controller.GetTypeInfo(), ActionName = action };
        var actionContext = new ActionContext(http, new RouteData(), descriptor, new ModelStateDictionary());
        var output = new OkObjectResult(response);
        await filter.OnActionExecutionAsync(new ActionExecutingContext(actionContext, [], arguments, new object()),
            () => Task.FromResult(new ActionExecutedContext(actionContext, [], new object()) { Result = output }));
        return Assert.IsType<QueryResult<BaseItemDto>>(output.Value);
    }
}
