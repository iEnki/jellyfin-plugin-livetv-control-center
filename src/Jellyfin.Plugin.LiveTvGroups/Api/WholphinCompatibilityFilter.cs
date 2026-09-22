using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LiveTvGroups.Channel;
using Jellyfin.Plugin.LiveTvGroups.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Api;

/// <summary>
/// Presents the plugin channel as ordinary folders to Wholphin, which cannot navigate Jellyfin channel items.
/// </summary>
public sealed class WholphinCompatibilityFilter : IAsyncActionFilter, IOrderedFilter
{
    private readonly ILibraryManager _library;
    private readonly ILogger<WholphinCompatibilityFilter> _logger;
    private readonly Func<bool> _targetsEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="WholphinCompatibilityFilter"/> class.
    /// </summary>
    public WholphinCompatibilityFilter(ILibraryManager library, ILogger<WholphinCompatibilityFilter> logger)
        : this(library, logger, WholphinTargetsEnabled)
    {
    }

    internal WholphinCompatibilityFilter(ILibraryManager library, ILogger<WholphinCompatibilityFilter> logger, Func<bool> targetsEnabled)
    {
        _library = library;
        _logger = logger;
        _targetsEnabled = targetsEnabled;
    }

    /// <summary>
    /// Runs outside the native-guide action filter so its response changes can be adapted for Wholphin.
    /// </summary>
    public int Order => -850;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var controller = descriptor?.ControllerTypeInfo.FullName;
        var isViews = controller == "Jellyfin.Api.Controllers.UserViewsController" && descriptor?.ActionName is "GetUserViews" or "GetUserViewsLegacy";
        var isItems = controller == "Jellyfin.Api.Controllers.ItemsController"
            && descriptor?.ActionName is "GetItems" or "GetItemsByUserIdLegacy";
        var principal = context.HttpContext.User;

        if ((!isViews && !isItems)
            || principal.Identity?.IsAuthenticated != true
            || !PlayerService.IsWholphinClient(principal.FindFirst("Jellyfin-Client")?.Value)
            || !string.Equals(principal.FindFirst("Jellyfin-IsApiKey")?.Value, "False", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(principal.FindFirst("Jellyfin-UserId")?.Value, out var userId)
            || userId == Guid.Empty
            || (context.ActionArguments.TryGetValue("userId", out var requested)
                && requested is Guid requestedUser && requestedUser != userId))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var executed = await next().ConfigureAwait(false);
        if (executed.Exception is not null || executed.Canceled
            || executed.Result is not ObjectResult result
            || (result.StatusCode is int status && status != 200)
            || result.Value is not QueryResult<BaseItemDto> query)
        {
            return;
        }

        try
        {
            var pluginId = GroupsChannel.GetInternalId(_library);
            if (isViews)
            {
                RewriteViews(result, query, pluginId);
                return;
            }

            if (!_targetsEnabled()
                || !context.ActionArguments.TryGetValue("parentId", out var parentValue)
                || parentValue is not Guid parentId)
            {
                return;
            }

            if (parentId == pluginId)
            {
                RewriteFolders(query.Items);
                return;
            }

            if (_library.GetItemById(parentId) is not Folder parent || parent.ChannelId != pluginId
                || (!GroupsChannel.TryParseGroupFolderId(parent.ExternalId, out _)
                    && !NativeGuideActionRoute.TryParse(parent.ExternalId, out _)))
            {
                return;
            }

            RewriteFolders(query.Items);
            if (context.HttpContext.Items.TryGetValue(NativeGuideActionFilter.ActionResultKey, out var appliedValue)
                && appliedValue is NativeGuideActionResult applied
                && query.Items.FirstOrDefault(item => item.IsFolder == true) is { } confirmation)
            {
                var language = context.HttpContext.Request.Headers.AcceptLanguage.ToString();
                confirmation.Name = applied.GroupId is null
                    ? PluginLocalization.Text("All channels selected for this TV", language)
                    : PluginLocalization.Text("Group selected for this TV: ", language) + applied.GroupName;
                confirmation.Overview = PluginLocalization.Text("Open Live TV → TV Guide", language) + ". "
                    + PluginLocalization.Text("The native TV guide opens in ordinary Live TV. Select TV Guide there. Reopen the app if its old channel list remains cached.", language);
                result.Value = new QueryResult<BaseItemDto>([confirmation])
                {
                    StartIndex = query.StartIndex,
                    TotalRecordCount = 1
                };
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning(error, "Wholphin channel compatibility failed; the original Jellyfin response was retained.");
        }
    }

    internal static bool WholphinTargetsEnabled()
        => Plugin.Instance?.Configuration.EnableWholphinTargets != false;

    private void RewriteViews(ObjectResult result, QueryResult<BaseItemDto> query, Guid pluginId)
    {
        var items = query.Items.ToList();
        var plugin = items.FirstOrDefault(item => item.Id == pluginId);
        if (plugin is null)
        {
            return;
        }

        if (!_targetsEnabled())
        {
            items.Remove(plugin);
            result.Value = new QueryResult<BaseItemDto>(items.ToArray())
            {
                StartIndex = query.StartIndex,
                TotalRecordCount = Math.Max(0, query.TotalRecordCount - 1)
            };
            return;
        }

        MakeFolder(plugin, BaseItemKind.CollectionFolder);
    }

    private static void RewriteFolders(IReadOnlyList<BaseItemDto> items)
    {
        foreach (var item in items.Where(item => item.IsFolder == true))
        {
            MakeFolder(item, BaseItemKind.Folder);
        }
    }

    private static void MakeFolder(BaseItemDto item, BaseItemKind kind)
    {
        item.Type = kind;
        // Wholphin maps CollectionType.folders to includeItemTypes=Folder. The
        // underlying Jellyfin items are still ChannelFolderItem objects until
        // this result filter runs, so that request would remove them too early.
        // Its supported neutral collection type keeps the child query unfiltered.
        item.CollectionType = CollectionType.unknown;
        item.IsFolder = true;
    }
}
